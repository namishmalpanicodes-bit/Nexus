/*
 * NEXUS V2 firmware - rev B (UNTESTED design submission)
 * Target: ESP32-P4NRW16X, ESP-IDF >= 5.3, esp_tinyusb 1.7
 *
 * What it does now:
 *   - 6 keys  -> USB HID keyboard F13..F18 (the desktop companion app maps them to any shortcut)
 *   - Encoder -> USB HID consumer control: clockwise = Volume Up, counter-clockwise = Volume Down
 *   - Encoder push -> Mute toggle
 *   - Turns on LDO channel 3 (2.5 V for the MIPI-DSI PHY) and the display backlight over I2C
 * Next steps: MIPI-DSI display + LVGL UI (stats, modes, notifications), CDC link to the companion app.
 * Pin map: see ../PIN_MAP.md
 */
#include <stdio.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "driver/gpio.h"
#include "driver/pulse_cnt.h"
#include "driver/i2c_master.h"
#include "esp_ldo_regulator.h"
#include "esp_log.h"
#include "tinyusb.h"
#include "class/hid/hid_device.h"

static const char *TAG = "nexus";

/* ---------------- pins (PIN_MAP.md) ---------------- */
static const gpio_num_t KEY_PINS[6] = {GPIO_NUM_23, GPIO_NUM_21, GPIO_NUM_19, GPIO_NUM_17, GPIO_NUM_15, GPIO_NUM_13};
#define ENC_A_PIN   GPIO_NUM_1
#define ENC_B_PIN   GPIO_NUM_2
#define ENC_SW_PIN  GPIO_NUM_3
#define LED_PIN     GPIO_NUM_5
#define I2C_SCL_PIN GPIO_NUM_28
#define I2C_SDA_PIN GPIO_NUM_29
#define BL_I2C_ADDR 0x45      /* Waveshare 7-DSI-TOUCH-A backlight controller */
#define BL_REG      0x96

#define DEBOUNCE_MS     8
#define POLL_MS         2
#define COUNTS_PER_STEP 4     /* full quadrature; adjust after counting the FlyRobo detents */

/* Default key codes: F13-F18, never used by normal apps, so the companion app can remap them freely */
static const uint8_t KEY_CODES[6] = {HID_KEY_F13, HID_KEY_F14, HID_KEY_F15, HID_KEY_F16, HID_KEY_F17, HID_KEY_F18};

/* ---------------- USB descriptors ---------------- */
enum { REPORT_ID_KEYBOARD = 1, REPORT_ID_CONSUMER = 2 };

static const uint8_t hid_report_descriptor[] = {
    TUD_HID_REPORT_DESC_KEYBOARD(HID_REPORT_ID(REPORT_ID_KEYBOARD)),
    TUD_HID_REPORT_DESC_CONSUMER(HID_REPORT_ID(REPORT_ID_CONSUMER)),
};

static const char *hid_string_descriptor[] = {
    (char[]){0x09, 0x04},   /* English */
    "NEXUS",                /* manufacturer */
    "NEXUS V2 Control Panel",
    "0001",
    "NEXUS HID",
};

#define TUSB_DESC_TOTAL_LEN (TUD_CONFIG_DESC_LEN + CFG_TUD_HID * TUD_HID_DESC_LEN)
static const uint8_t hid_configuration_descriptor[] = {
    TUD_CONFIG_DESCRIPTOR(1, 1, 0, TUSB_DESC_TOTAL_LEN, TUSB_DESC_CONFIG_ATT_REMOTE_WAKEUP, 100),
    TUD_HID_DESCRIPTOR(0, 4, false, sizeof(hid_report_descriptor), 0x81, 16, 10),
};

uint8_t const *tud_hid_descriptor_report_cb(uint8_t instance) { (void)instance; return hid_report_descriptor; }
uint16_t tud_hid_get_report_cb(uint8_t instance, uint8_t report_id, hid_report_type_t report_type, uint8_t *buffer, uint16_t reqlen)
{ (void)instance; (void)report_id; (void)report_type; (void)buffer; (void)reqlen; return 0; }
void tud_hid_set_report_cb(uint8_t instance, uint8_t report_id, hid_report_type_t report_type, uint8_t const *buffer, uint16_t bufsize)
{ (void)instance; (void)report_id; (void)report_type; (void)buffer; (void)bufsize; }

/* ---------------- HID helpers ---------------- */
static void send_key(uint8_t keycode, bool pressed)
{
    if (!tud_mounted()) return;
    uint8_t keys[6] = {0};
    if (pressed) keys[0] = keycode;
    tud_hid_keyboard_report(REPORT_ID_KEYBOARD, 0, keys);
}

static void send_consumer(uint16_t usage)
{
    if (!tud_mounted()) return;
    tud_hid_report(REPORT_ID_CONSUMER, &usage, sizeof(usage));
    vTaskDelay(pdMS_TO_TICKS(10));
    uint16_t none = 0;
    tud_hid_report(REPORT_ID_CONSUMER, &none, sizeof(none));
    vTaskDelay(pdMS_TO_TICKS(10));
}

/* ---------------- hardware init ---------------- */
static pcnt_unit_handle_t enc_unit;

static void encoder_init(void)
{
    pcnt_unit_config_t unit_cfg = {.high_limit = 1000, .low_limit = -1000};
    ESP_ERROR_CHECK(pcnt_new_unit(&unit_cfg, &enc_unit));
    pcnt_glitch_filter_config_t filter = {.max_glitch_ns = 1000};
    ESP_ERROR_CHECK(pcnt_unit_set_glitch_filter(enc_unit, &filter));

    pcnt_channel_handle_t ch_a, ch_b;
    pcnt_chan_config_t cfg_a = {.edge_gpio_num = ENC_A_PIN, .level_gpio_num = ENC_B_PIN};
    pcnt_chan_config_t cfg_b = {.edge_gpio_num = ENC_B_PIN, .level_gpio_num = ENC_A_PIN};
    ESP_ERROR_CHECK(pcnt_new_channel(enc_unit, &cfg_a, &ch_a));
    ESP_ERROR_CHECK(pcnt_new_channel(enc_unit, &cfg_b, &ch_b));
    ESP_ERROR_CHECK(pcnt_channel_set_edge_action(ch_a, PCNT_CHANNEL_EDGE_ACTION_DECREASE, PCNT_CHANNEL_EDGE_ACTION_INCREASE));
    ESP_ERROR_CHECK(pcnt_channel_set_level_action(ch_a, PCNT_CHANNEL_LEVEL_ACTION_KEEP, PCNT_CHANNEL_LEVEL_ACTION_INVERSE));
    ESP_ERROR_CHECK(pcnt_channel_set_edge_action(ch_b, PCNT_CHANNEL_EDGE_ACTION_INCREASE, PCNT_CHANNEL_EDGE_ACTION_DECREASE));
    ESP_ERROR_CHECK(pcnt_channel_set_level_action(ch_b, PCNT_CHANNEL_LEVEL_ACTION_KEEP, PCNT_CHANNEL_LEVEL_ACTION_INVERSE));
    ESP_ERROR_CHECK(pcnt_unit_enable(enc_unit));
    ESP_ERROR_CHECK(pcnt_unit_clear_count(enc_unit));
    ESP_ERROR_CHECK(pcnt_unit_start(enc_unit));
}

static void inputs_init(void)
{
    uint64_t mask = (1ULL << ENC_SW_PIN);
    for (int i = 0; i < 6; i++) mask |= (1ULL << KEY_PINS[i]);
    gpio_config_t in = {.pin_bit_mask = mask, .mode = GPIO_MODE_INPUT, .pull_up_en = GPIO_PULLUP_ENABLE};
    ESP_ERROR_CHECK(gpio_config(&in));
    gpio_config_t out = {.pin_bit_mask = (1ULL << LED_PIN), .mode = GPIO_MODE_OUTPUT};
    ESP_ERROR_CHECK(gpio_config(&out));
}

static void display_power_init(void)
{
    /* LDO channel 3 -> 2.5 V for the MIPI-DSI PHY (see schematic note) */
    static esp_ldo_channel_handle_t ldo_mipi;
    esp_ldo_channel_config_t ldo_cfg = {.chan_id = 3, .voltage_mv = 2500};
    ESP_ERROR_CHECK(esp_ldo_acquire_channel(&ldo_cfg, &ldo_mipi));

    /* Backlight on (Waveshare: register 0x96 at I2C address 0x45, 0..255) */
    i2c_master_bus_handle_t bus;
    i2c_master_bus_config_t bus_cfg = {
        .i2c_port = I2C_NUM_0, .sda_io_num = I2C_SDA_PIN, .scl_io_num = I2C_SCL_PIN,
        .clk_source = I2C_CLK_SRC_DEFAULT, .glitch_ignore_cnt = 7,
    };
    ESP_ERROR_CHECK(i2c_new_master_bus(&bus_cfg, &bus));
    i2c_master_dev_handle_t bl;
    i2c_device_config_t dev_cfg = {.dev_addr_length = I2C_ADDR_BIT_LEN_7, .device_address = BL_I2C_ADDR, .scl_speed_hz = 100000};
    ESP_ERROR_CHECK(i2c_master_bus_add_device(bus, &dev_cfg, &bl));
    uint8_t cmd[2] = {BL_REG, 0xFF};
    if (i2c_master_transmit(bl, cmd, sizeof(cmd), 100) != ESP_OK) ESP_LOGW(TAG, "backlight controller not responding");
    /* TODO next: esp_lcd MIPI-DSI bus + waveshare ILI9881C panel driver + LVGL UI */
}

/* ---------------- main loop: debounce keys, read encoder ---------------- */
static void input_task(void *arg)
{
    bool stable[7] = {0}, last_raw[7] = {0};
    TickType_t changed_at[7] = {0};
    int enc_acc = 0, last_count = 0;

    for (;;) {
        TickType_t now = xTaskGetTickCount();
        for (int i = 0; i < 7; i++) {
            gpio_num_t pin = (i < 6) ? KEY_PINS[i] : ENC_SW_PIN;
            bool raw = (gpio_get_level(pin) == 0);              /* active LOW */
            if (raw != last_raw[i]) { last_raw[i] = raw; changed_at[i] = now; }
            if (raw != stable[i] && (now - changed_at[i]) >= pdMS_TO_TICKS(DEBOUNCE_MS)) {
                stable[i] = raw;
                if (i < 6) {
                    send_key(KEY_CODES[i], raw);
                    ESP_LOGI(TAG, "key %d %s", i + 1, raw ? "down" : "up");
                } else if (raw) {
                    send_consumer(HID_USAGE_CONSUMER_MUTE);
                    ESP_LOGI(TAG, "mute");
                }
            }
        }

        int count = 0;
        pcnt_unit_get_count(enc_unit, &count);
        enc_acc += count - last_count;
        last_count = count;
        if (count > 900 || count < -900) { pcnt_unit_clear_count(enc_unit); last_count = 0; }
        while (enc_acc >= COUNTS_PER_STEP)  { send_consumer(HID_USAGE_CONSUMER_VOLUME_INCREMENT); enc_acc -= COUNTS_PER_STEP; }
        while (enc_acc <= -COUNTS_PER_STEP) { send_consumer(HID_USAGE_CONSUMER_VOLUME_DECREMENT); enc_acc += COUNTS_PER_STEP; }

        gpio_set_level(LED_PIN, tud_mounted());
        vTaskDelay(pdMS_TO_TICKS(POLL_MS));
    }
}

void app_main(void)
{
    ESP_LOGI(TAG, "NEXUS V2 rev B starting");
    inputs_init();
    encoder_init();
    display_power_init();

    const tinyusb_config_t tusb_cfg = {
        .device_descriptor = NULL,                       /* default VID/PID from esp_tinyusb */
        .string_descriptor = hid_string_descriptor,
        .string_descriptor_count = sizeof(hid_string_descriptor) / sizeof(hid_string_descriptor[0]),
        .external_phy = false,
#if (TUD_OPT_HIGH_SPEED)
        .fs_configuration_descriptor = hid_configuration_descriptor,
        .hs_configuration_descriptor = hid_configuration_descriptor,
        .qualifier_descriptor = NULL,
#else
        .configuration_descriptor = hid_configuration_descriptor,
#endif
    };
    ESP_ERROR_CHECK(tinyusb_driver_install(&tusb_cfg));
    ESP_LOGI(TAG, "USB HID ready");

    xTaskCreate(input_task, "inputs", 4096, NULL, 5, NULL);
}
