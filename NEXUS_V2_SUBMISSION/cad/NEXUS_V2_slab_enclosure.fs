// ===== NEXUS V2 — slab + tilt hinge + weighted base (Onshape FeatureScript) =====
// HOW TO USE: create a Feature Studio, KEEP its first two lines ("FeatureScript ####;" + import),
// delete the rest and paste everything below. Commit. Then in a Part Studio: Custom features -> "NEXUS V2 Slab".
//
// Model coordinates (front view): X = left->right, Y = up, Z = depth (back = 0, front face = D).
// Parts are created laid out flat, NOT assembled at an angle: the slab parts sit at Y >= 0, the base sits below
// (Y < 0) lying on its bottom. In an Onshape Assembly, mate the hinges with a Revolute mate (limits 15–70 deg).
//
// Layout: 7" Waveshare display on the LEFT; 54 x 118 mm control/MCU board on the RIGHT, beside the display
// (so the slab is only as thick as the display). 2x3 keys at 19 mm pitch upper-right, 24 mm knob centred below.
// USB-C exits the BOTTOM edge of the slab next to the right hinge.
//
// PROVISIONAL (verify before printing for real): display depth (measure yours -> parameter), C&K PTS526 switch
// height/plunger, FlyRobo EC11 shaft (15 mm from PCB seating plane assumed), hinge hole pattern (parameters).

const H = 128.24;              // slab height (display 120.24 + 2 x 4 mm border)
const W = 254.0;               // slab width
const WALL = 2.4; const SKIN = 2.0; const BACKT = 1.6; const RO = 6.0;
const GX = 4.0; const GY = 4.0;                    // display glass top-left (y measured DOWN from the top edge)
const AAX = GX + 17.16; const AAY = GY + 16.12; const AAW = 155.0; const AAH = 88.0;   // active area
const PX = 196.0; const PY = (128.24 - 118.0) / 2; // board top-left (board 54 x 118)
const BASE_D = 100.0; const BASE_T = 12.0; const BASE_GAP = 25.0;

function nxMM(x, y, z) returns Vector { return vector(x, y, z) * millimeter; }
function nxY(ydown) returns number { return H - ydown; }
function nxNextId(id is Id, n is box) returns Id { n[] = n[] + 1; return id + ("p" ~ toString(n[])); }
function nxCube(context is Context, id is Id, n is box, x0, y0, z0, x1, y1, z1) returns Query
{
    const i = nxNextId(id, n);
    fCuboid(context, i, { "corner1" : nxMM(x0, y0, z0), "corner2" : nxMM(x1, y1, z1) });
    return qCreatedBy(i, EntityType.BODY);
}
function nxCyl(context is Context, id is Id, n is box, x, y, z0, z1, r) returns Query
{
    const i = nxNextId(id, n);
    fCylinder(context, i, { "bottomCenter" : nxMM(x, y, z0), "topCenter" : nxMM(x, y, z1), "radius" : r * millimeter });
    return qCreatedBy(i, EntityType.BODY);
}
function nxCylY(context is Context, id is Id, n is box, x, z, y0, y1, r) returns Query
{
    const i = nxNextId(id, n);
    fCylinder(context, i, { "bottomCenter" : nxMM(x, y0, z), "topCenter" : nxMM(x, y1, z), "radius" : r * millimeter });
    return qCreatedBy(i, EntityType.BODY);
}
function nxUnite(context is Context, id is Id, n is box, parts is array) returns Query
{
    const q = qUnion(parts);
    if (size(parts) > 1)
        opBoolean(context, nxNextId(id, n), { "tools" : q, "operationType" : BooleanOperationType.UNION });
    return q;
}
function nxCut(context is Context, id is Id, n is box, target is Query, tools is array)
{
    opBoolean(context, nxNextId(id, n), { "targets" : target, "tools" : qUnion(tools), "operationType" : BooleanOperationType.SUBTRACTION });
}
function nxRBlock(context is Context, id is Id, n is box, x0, y0, x1, y1, z0, z1, r) returns Query
{
    return nxUnite(context, id, n, [
        nxCube(context, id, n, x0 + r, y0, z0, x1 - r, y1, z1),
        nxCube(context, id, n, x0, y0 + r, z0, x1, y1 - r, z1),
        nxCyl(context, id, n, x0 + r, y0 + r, z0, z1, r), nxCyl(context, id, n, x1 - r, y0 + r, z0, z1, r),
        nxCyl(context, id, n, x0 + r, y1 - r, z0, z1, r), nxCyl(context, id, n, x1 - r, y1 - r, z0, z1, r)]);
}
function nxName(context is Context, q is Query, name is string)
{
    setProperty(context, { "entities" : q, "propertyType" : PropertyType.NAME, "value" : name });
}

annotation { "Feature Type Name" : "NEXUS V2 Slab" }
export const nexusSlab = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Display depth (measure yours: glass front to back-most point)" }
        isLength(definition.dispDepth, { (millimeter) : [5, 14.8, 25] } as LengthBoundSpec);
        annotation { "Name" : "Hinge leaf hole spacing" }
        isLength(definition.hingePitch, { (millimeter) : [8, 20, 40] } as LengthBoundSpec);
        annotation { "Name" : "Hinge screw pilot diameter" }
        isLength(definition.hingePilot, { (millimeter) : [1.5, 2.5, 4] } as LengthBoundSpec);
        annotation { "Name" : "Make keycaps", "Default" : true }
        definition.caps is boolean;
        annotation { "Name" : "Make knob", "Default" : true }
        definition.knob is boolean;
        annotation { "Name" : "Make base", "Default" : true }
        definition.base is boolean;
    }
    {
        var n = new box(0);
        const DD = definition.dispDepth / millimeter;
        const D = max(SKIN + DD + 0.4 + BACKT, 13.6);    // slab thickness (13.6 = minimum for the control board stack)
        const PCBF = D - SKIN - 7.0;                     // PCB component face (EC11 body ~6.5 mm + 0.5)
        const PCBB = PCBF - 1.6;
        const SWTOP = PCBF + 1.5;                        // PTS526 SPG15 actuator top
        const HP = definition.hingePitch / millimeter;
        const HPR = definition.hingePilot / millimeter / 2;
        const hingeX = [45.0, 165.0];               // both under the display: keeps the board area free for vents/USB
        const BOSSH = max(2.0, min(6.0, D - SKIN - 6.4 - BACKT));   // stay below the display glass edge

        var keys = [];
        for (var i = 0; i < 6; i += 1)
            keys = append(keys, [PX + 20.5 + 19 * (i % 2), nxY(PY + 12 + 19 * floor(i / 2))]);
        const encX = PX + 30.0; const encY = nxY(PY + 74.0);
        const usbX = PX + 33.0;
        const pcbHoles = [[PX + 4, PY + 4], [PX + 50, PY + 4], [PX + 4, PY + 114], [PX + 50, PY + 114]];
        const corner = [[7, 7, 1, 7], [7, H - 7, 1, 7]];   // left-side back-cover screw bosses (x, Y, bridge x0, x1)

        // ================= FRONT SHELL =================
        var front = nxRBlock(context, id, n, 0, 0, W, H, BACKT, D, RO);
        nxCut(context, id, n, front, [nxRBlock(context, id, n, WALL, WALL, W - WALL, H - WALL, BACKT - 1, D - SKIN, RO - WALL)]);
        var adds = [];
        for (var p in [pcbHoles[1], pcbHoles[3]])     // right PCB holes: long screws from the back end here
            adds = append(adds, nxCyl(context, id, n, p[0], nxY(p[1]), PCBF, D - SKIN + 0.5, 3.0));
        for (var c in corner)
        {
            adds = append(adds, nxCyl(context, id, n, c[0], c[1], BACKT, BACKT + BOSSH, 3.0));
            adds = append(adds, nxCube(context, id, n, c[2], c[1] - 2, BACKT, c[3], c[1] + 2, BACKT + BOSSH));
        }
        front = nxUnite(context, id, n, concatenateArrays([[front], adds]));
        var fh = [nxCube(context, id, n, AAX - 0.5, nxY(AAY + AAH + 0.5), D - SKIN - 1, AAX + AAW + 0.5, nxY(AAY - 0.5), D + 1)];
        for (var k in keys)
            fh = append(fh, nxCube(context, id, n, k[0] - 8.8, k[1] - 8.8, D - SKIN - 1, k[0] + 8.8, k[1] + 8.8, D + 1));
        fh = append(fh, nxCyl(context, id, n, encX, encY, D - SKIN - 1, D + 1, 4.0));
        // USB-C slot in the BOTTOM wall
        const uz = PCBF + 1.6;
        // 13 x 7.5 mm obround: big enough for typical USB-C plug overmolds, which must reach the receptacle ~4.8 mm inside
        fh = append(fh, nxCube(context, id, n, usbX - 2.75, -1, uz - 3.75, usbX + 2.75, PY + 1, uz + 3.75));
        fh = append(fh, nxCylY(context, id, n, usbX - 2.75, uz, -1, PY + 1, 3.75));
        fh = append(fh, nxCylY(context, id, n, usbX + 2.75, uz, -1, PY + 1, 3.75));
        for (var p in [pcbHoles[1], pcbHoles[3]])
            fh = append(fh, nxCyl(context, id, n, p[0], nxY(p[1]), PCBF - 1, D - SKIN - 1, 1.25));
        for (var c in corner)
            fh = append(fh, nxCyl(context, id, n, c[0], c[1], BACKT - 1, BACKT + BOSSH - 1, 1.25));
        nxCut(context, id, n, front, fh);
        nxName(context, front, "Front shell");

        // ================= BACK COVER (structural: carries the hinges) =================
        var bparts = [nxRBlock(context, id, n, 0, 0, W, H, 0, BACKT, RO)];
        for (var p in pcbHoles)
            bparts = append(bparts, nxCyl(context, id, n, p[0], nxY(p[1]), BACKT - 0.5, PCBB, 3.0));
        // hinge pads on the OUTSIDE (Z < 0), at the bottom edge
        for (var hx in hingeX)
            bparts = append(bparts, nxCube(context, id, n, hx - 16, 2, -4, hx + 16, 26, 0.5));
        // display support posts (only if there is a gap behind the display)
        const gap = (D - SKIN - DD) - BACKT;
        if (gap > 0.6)
            for (var p in [[45, 40], [45, 85], [150, 40], [150, 85]])
                bparts = append(bparts, nxCyl(context, id, n, p[0], nxY(p[1]), BACKT - 0.5, BACKT + gap - 0.2, 4.0));
        var back = nxUnite(context, id, n, bparts);
        var bh = [];
        for (var p in [pcbHoles[1], pcbHoles[3]])
            bh = append(bh, nxCyl(context, id, n, p[0], nxY(p[1]), -5, PCBB + 1, 1.7));
        for (var p in [pcbHoles[0], pcbHoles[2]])
            bh = append(bh, nxCyl(context, id, n, p[0], nxY(p[1]), BACKT - 1, PCBB + 1, 1.25));
        for (var c in corner)
            bh = append(bh, nxCyl(context, id, n, c[0], c[1], -5, BACKT + 1, 1.7));
        for (var hx in hingeX)
            for (var s in [-1, 1])
                bh = append(bh, nxCyl(context, id, n, hx + s * HP / 2, 14, -5, 0.4, HPR));
        // debug-port window (J3, fitted from the back), service pin-holes (BOOT/RESET on the back), vents
        bh = append(bh, nxCube(context, id, n, PX + 46, nxY(PY + 30), -5, PX + 51, nxY(PY + 7.5), BACKT + 1));
        for (var p in [[PX + 45, PY + 40], [PX + 45, PY + 50]])
            bh = append(bh, nxCyl(context, id, n, p[0], nxY(p[1]), -5, BACKT + 1, 1.2));
        for (var k = 0; k < 4; k += 1)
            bh = append(bh, nxCube(context, id, n, PX + 10 + 4 * k, nxY(PY + 110), -5, PX + 12 + 4 * k, nxY(PY + 90), BACKT + 1));
        nxCut(context, id, n, back, bh);
        nxName(context, back, "Back cover (hinge side)");

        // ================= KEYCAPS (17 mm, plunger onto the tact switch) =================
        if (definition.caps)
        {
            var kk = 0;
            for (var k in keys)
            {
                kk += 1;
                nxName(context, nxUnite(context, id, n, [
                    nxCube(context, id, n, k[0] - 8.5, k[1] - 8.5, D - SKIN - 1.0, k[0] + 8.5, k[1] + 8.5, D + 2.0),
                    nxCube(context, id, n, k[0] - 9.2, k[1] - 9.2, D - SKIN - 1.5, k[0] + 9.2, k[1] + 9.2, D - SKIN - 0.2),
                    nxCyl(context, id, n, k[0], k[1], SWTOP + 0.2, D - SKIN - 1.4, 1.8)]), "Keycap " ~ toString(kk));
            }
        }
        // ================= KNOB 24 mm (counterbored over the EC11 bushing) =================
        if (definition.knob)
        {
            // EC11: body ~6.5 mm, M7 bushing 5-7 mm, 15 mm shaft above the body -> bushing top up to PCBF+13.5,
            // shaft tip ~PCBF+21.5 (= D+12.5). Knob: counterbore over the bushing + D-bore deep enough for the full shaft.
            const ktop = D + 15.0;
            var kn = nxCyl(context, id, n, encX, encY, D + 1.0, ktop, 12.0);
            nxCut(context, id, n, kn, [nxCyl(context, id, n, encX, encY, D + 0.5, D + 6.5, 4.3),
                                       nxCyl(context, id, n, encX, encY, D + 4.0, ktop - 1.5, 3.05)]);
            kn = nxUnite(context, id, n, [kn, nxCube(context, id, n, encX + 1.55, encY - 3.1, D + 6.5, encX + 3.1, encY + 3.1, ktop - 1.4)]);
            nxName(context, kn, "Knob 24mm");
        }
        // ================= WEIGHTED BASE (laid flat below the slab) =================
        if (definition.base)
        {
            const y1 = -BASE_GAP; const y0 = y1 - BASE_D;       // y1 = FRONT edge of the base, y0 = back edge
            var base = nxRBlock(context, id, n, 0, y0, W, y1, 0, BASE_T, RO);
            var risers = [];
            for (var hx in hingeX)
                risers = append(risers, nxCube(context, id, n, hx - 16, y1 - 42, BASE_T - 0.5, hx + 16, y1 - 28, BASE_T + 6));
            base = nxUnite(context, id, n, concatenateArrays([[base], risers]));
            var bc = [];
            // steel plate pocket from below, FRONT half (200 x 60 x 3.2 mm, ~280 g of 3 mm steel)
            bc = append(bc, nxCube(context, id, n, W / 2 - 100.3, y1 - 70.3, -1, W / 2 + 100.3, y1 - 9.7, 3.3));
            // TPU anti-slip strips (6 x 1 mm grooves) front and back
            bc = append(bc, nxCube(context, id, n, 10, y1 - 8, -1, W - 10, y1 - 2, 1.0));
            bc = append(bc, nxCube(context, id, n, 10, y0 + 2, -1, W - 10, y0 + 8, 1.0));
            // hinge pilot holes in the risers
            for (var hx in hingeX)
                for (var s in [-1, 1])
                    bc = append(bc, nxCyl(context, id, n, hx + s * HP / 2, y1 - 35, BASE_T, BASE_T + 7, HPR));
            // USB plug bay: the slab hangs on the hinges ~6 mm above the base (riser height), its USB-C port points down.
            // An L-shaped USB-C plug drops into this 16 x 26 x 7 mm pocket, the cable runs back through a 9 x 7 mm channel
            // and exits at the BACK edge of the base. Floor above the steel-plate pocket = 1.7 mm.
            bc = append(bc, nxCube(context, id, n, usbX - 8, y1 - 34, BASE_T - 7, usbX + 8, y1 - 8, BASE_T + 1));
            bc = append(bc, nxCube(context, id, n, usbX - 4.5, y0 - 1, BASE_T - 7, usbX + 4.5, y1 - 33, BASE_T + 1));
            nxCut(context, id, n, base, bc);
            nxName(context, base, "Weighted base");
        }
    });
