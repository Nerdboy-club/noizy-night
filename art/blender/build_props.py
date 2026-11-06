"""
Procedural stylized props (rooms, hub) for Silent Night.

  blender --background --python art/blender/build_props.py

Each prop -> Unity/Assets/_Project/Art/Environment/<Room>/<Name>.fbx (metres, origin on the floor at the
prop's centre, front faces -Y in Blender = +Z in Unity). Also writes art/blender/props_preview.png.
"""
import bpy
import math
import os

try:
    ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
except NameError:
    ROOT = "C:/Vs-code/gamedev/noizy-night"
ENV = os.path.join(ROOT, "unity", "SilentNight", "Assets", "_Project", "Art", "Environment")
PREVIEW = os.path.join(ROOT, "art", "blender", "props_preview.png")

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# ------------------------------------------------------------------ materials
_m = {}


def M(name, rgb, rough=0.9, emit=0.0):
    if name in _m:
        return _m[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    if emit:
        b.inputs["Emission Color"].default_value = (*rgb, 1)
        b.inputs["Emission Strength"].default_value = emit
    m.diffuse_color = (*rgb, 1)
    _m[name] = m
    return m


WOOD = M("Wood", (0.50, 0.30, 0.16))
DWOOD = M("DarkWood", (0.30, 0.17, 0.09))
LWOOD = M("LightWood", (0.72, 0.52, 0.30))
CREAM = M("Cream", (0.93, 0.88, 0.76))
WHITE = M("White", (0.95, 0.95, 0.93))
RED = M("Red", (0.78, 0.10, 0.12))
GREEN = M("Green", (0.10, 0.42, 0.20))
DGREEN = M("DarkGreen", (0.06, 0.28, 0.14))
BLUE = M("Blue", (0.18, 0.32, 0.65))
BRICK = M("Brick", (0.55, 0.22, 0.16))
STONE = M("Stone", (0.50, 0.48, 0.46))
METAL = M("Metal", (0.55, 0.57, 0.60), rough=0.4)
DARK = M("Dark", (0.07, 0.06, 0.07))
GOLD = M("Gold", (0.95, 0.75, 0.22), rough=0.35)
GLOW = M("Glow", (1.0, 0.72, 0.30), emit=4.0)
FIRE = M("Fire", (1.0, 0.40, 0.08), emit=6.0)
SCREEN = M("Screen", (0.25, 0.55, 0.75), emit=1.5)
LEATHER = M("Leather", (0.40, 0.18, 0.10))
FABRIC = M("FabricTeal", (0.20, 0.45, 0.45))
QUILT = M("Quilt", (0.75, 0.30, 0.25))
CARD = M("Cardboard", (0.62, 0.46, 0.28))
TARP = M("Tarp", (0.35, 0.42, 0.50))
ICE = M("Ice", (0.65, 0.85, 0.95), rough=0.1)
ORANGE = M("Orange", (0.90, 0.50, 0.15))

# ------------------------------------------------------------------ helpers
parts = []


def _fin(o, mat, bevel=0.0, smooth=False):
    ctx = bpy.context
    ctx.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if bevel > 0:
        md = o.modifiers.new("Bevel", "BEVEL")
        md.width = bevel
        md.segments = 2
        md.limit_method = "ANGLE"
        bpy.ops.object.modifier_apply(modifier="Bevel")
    if smooth:
        bpy.ops.object.shade_smooth()
    o.data.materials.append(mat)
    o.select_set(False)
    parts.append(o)
    return o


def box(c, s, mat, bevel=0.015, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=c)
    o = bpy.context.view_layer.objects.active
    o.scale = s
    o.rotation_euler = rot
    return _fin(o, mat, min(bevel, min(s) * 0.4))


def cyl(c, r, h, mat, verts=16, rot=(0, 0, 0), smooth=True, r2=None):
    if r2 is None:
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=h, location=c)
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=h, location=c)
    o = bpy.context.view_layer.objects.active
    o.rotation_euler = rot
    return _fin(o, mat, 0.0, smooth)


def sph(c, s, mat, seg=14, rings=9):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=1, location=c)
    o = bpy.context.view_layer.objects.active
    o.scale = s if hasattr(s, "__len__") else (s, s, s)
    return _fin(o, mat, 0.0, True)


def tor(c, major, minor, mat, rot=(0, 0, 0), scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=20, minor_segments=8, location=c)
    o = bpy.context.view_layer.objects.active
    o.rotation_euler = rot
    o.scale = scale
    return _fin(o, mat, 0.0, True)


PROPS = []  # (room, name, fn)


def prop(room, name):
    def deco(fn):
        PROPS.append((room, name, fn))
        return fn
    return deco


# ------------------------------------------------------------------ Living room
@prop("LivingRoom", "ChristmasTree")
def _():
    cyl((0, 0, 0.12), 0.32, 0.24, RED, 16)
    cyl((0, 0, 0.45), 0.07, 0.5, DWOOD, 10)
    for i, (r, z, h) in enumerate([(0.95, 0.65, 0.7), (0.78, 1.1, 0.65), (0.6, 1.5, 0.6), (0.4, 1.85, 0.55)]):
        cyl((0, 0, z), r, h, GREEN if i % 2 == 0 else DGREEN, 20, r2=0.04)
    sph((0, 0, 2.28), 0.09, GOLD, 8, 6)
    cyl((0, 0, 2.2), 0.05, 0.12, GOLD, 6, r2=0.0)
    cols = [RED, GOLD, BLUE, WHITE]
    for i in range(18):
        a = i * 2.4
        z = 0.75 + (i % 6) * 0.25
        r = max(0.12, 0.85 - (z - 0.6) * 0.45)
        sph((math.cos(a) * r, math.sin(a) * r, z), 0.06, cols[i % 4], 8, 6)
    for z, r in ((0.95, 0.62), (1.4, 0.45), (1.8, 0.28)):
        tor((0, 0, z), r, 0.018, GOLD, scale=(1, 1, 1))


@prop("LivingRoom", "Fireplace")
def _():
    box((0, 0.15, 0.9), (1.6, 0.5, 1.8), BRICK, 0.03)
    box((0, -0.12, 0.5), (0.8, 0.14, 0.75), DARK, 0.02)
    box((0, 0.0, 0.2), (1.2, 0.05, 0.1), STONE)
    cone = cyl((0, -0.1, 0.32), 0.17, 0.4, FIRE, 8, r2=0.0)
    cyl((-0.1, -0.1, 0.25), 0.12, 0.3, GLOW, 8, r2=0.0)
    box((0, -0.05, 1.85), (1.8, 0.62, 0.12), DWOOD, 0.02)
    box((0, 0.2, 2.5), (0.9, 0.4, 1.3), BRICK, 0.03)
    for x, c in ((-0.55, RED), (0.0, GREEN), (0.55, RED)):
        box((x, -0.2, 1.62), (0.15, 0.06, 0.28), c, 0.02)
        box((x, -0.2, 1.5), (0.15, 0.06, 0.06), WHITE, 0.01)


@prop("LivingRoom", "Armchair")
def _():
    box((0, 0, 0.25), (0.9, 0.85, 0.3), FABRIC, 0.06)
    box((0, 0.35, 0.65), (0.9, 0.2, 0.7), FABRIC, 0.06)
    box((-0.45, 0, 0.45), (0.14, 0.8, 0.5), FABRIC, 0.05)
    box((0.45, 0, 0.45), (0.14, 0.8, 0.5), FABRIC, 0.05)
    box((0, -0.05, 0.47), (0.62, 0.62, 0.14), CREAM, 0.05)
    box((0.12, -0.05, 0.62), (0.6, 0.5, 0.05), QUILT, 0.02, rot=(0, 0, 0.3))
    for x in (-0.38, 0.38):
        for y in (-0.33, 0.33):
            cyl((x, y, 0.05), 0.04, 0.1, DWOOD, 8)


@prop("LivingRoom", "RoundRug")
def _():
    cyl((0, 0, 0.012), 1.1, 0.024, RED, 32)
    cyl((0, 0, 0.02), 0.85, 0.024, WHITE, 32)
    cyl((0, 0, 0.028), 0.6, 0.024, GREEN, 32)
    cyl((0, 0, 0.036), 0.3, 0.024, RED, 32)


@prop("LivingRoom", "Bookshelf")
def _():
    box((0, 0.1, 1.0), (1.1, 0.35, 2.0), WOOD, 0.02)
    for z in (0.35, 0.85, 1.35, 1.8):
        box((0, 0.02, z), (1.0, 0.3, 0.03), DWOOD, 0.005)
    cols = [RED, BLUE, GREEN, CREAM, ORANGE, QUILT]
    for row, z in enumerate((0.55, 1.05, 1.55)):
        x = -0.42
        for i in range(8):
            w = 0.06 + 0.02 * ((i + row) % 3)
            box((x + w / 2, 0.02, z), (w, 0.22, 0.28 - 0.03 * ((i * row) % 3)), cols[(i + row) % 6], 0.005)
            x += w + 0.015
    box((0, 0.1, 2.03), (1.2, 0.4, 0.05), DWOOD, 0.01)


@prop("LivingRoom", "TVStand")
def _():
    box((0, 0, 0.25), (1.3, 0.45, 0.5), WOOD, 0.02)
    box((0, -0.01, 0.78), (1.0, 0.1, 0.6), DARK, 0.02)
    box((0, -0.07, 0.78), (0.9, 0.02, 0.5), SCREEN, 0.005)
    box((0, 0, 0.53), (0.35, 0.18, 0.06), DARK, 0.01)


# ------------------------------------------------------------------ Hallway
@prop("Hallway", "GrandfatherClock")
def _():
    box((0, 0, 0.12), (0.55, 0.38, 0.24), DWOOD, 0.02)
    box((0, 0, 0.95), (0.42, 0.3, 1.5), WOOD, 0.02)
    box((0, 0, 1.78), (0.5, 0.34, 0.4), DWOOD, 0.03)
    cyl((0, -0.17, 1.78), 0.15, 0.03, CREAM, 24, rot=(math.pi / 2, 0, 0))
    box((0.0, -0.19, 1.82), (0.015, 0.01, 0.1), DARK, 0.002)
    box((0.04, -0.19, 1.78), (0.08, 0.01, 0.015), DARK, 0.002)
    box((0, -0.16, 1.0), (0.26, 0.02, 0.8), DARK, 0.005)
    cyl((0, -0.17, 0.75), 0.07, 0.02, GOLD, 16, rot=(math.pi / 2, 0, 0))
    cyl((0, -0.165, 0.95), 0.012, 0.35, GOLD, 8)
    cyl((0, 0, 2.06), 0.12, 0.1, DWOOD, 8, r2=0.02)


@prop("Hallway", "CoatRack")
def _():
    cyl((0, 0, 0.04), 0.25, 0.08, DWOOD, 16)
    cyl((0, 0, 0.9), 0.035, 1.7, DWOOD, 8)
    for i in range(4):
        a = i * math.pi / 2
        box((math.cos(a) * 0.1, math.sin(a) * 0.1, 1.65), (0.2, 0.03, 0.03), DWOOD, 0.005, rot=(0, 0, a))
    cyl((0.2, 0.0, 1.35), 0.2, 0.75, BLUE, 12, r2=0.1)
    cyl((-0.15, 0.1, 1.4), 0.17, 0.65, RED, 12, r2=0.08)
    sph((0, 0, 1.78), (0.15, 0.15, 0.09), GREEN, 10, 6)


@prop("Hallway", "ShoeRack")
def _():
    box((0, 0, 0.3), (0.8, 0.3, 0.04), WOOD, 0.005)
    box((0, 0, 0.03), (0.8, 0.3, 0.04), WOOD, 0.005)
    box((0, 0, 0.58), (0.8, 0.3, 0.04), WOOD, 0.005)
    for x in (-0.38, 0.38):
        box((x, 0, 0.3), (0.04, 0.3, 0.6), DWOOD, 0.005)
    for i, x in enumerate((-0.2, 0.15)):
        sph((x, -0.02, 0.4), (0.07, 0.12, 0.08), BOOT if False else LEATHER)
        sph((x, -0.02, 0.15), (0.07, 0.12, 0.09), RED)


@prop("Hallway", "SideTableLamp")
def _():
    box((0, 0, 0.7), (0.5, 0.35, 0.04), WOOD, 0.01)
    box((0, 0, 0.35), (0.46, 0.3, 0.04), DWOOD, 0.005)
    for x in (-0.21, 0.21):
        for y in (-0.14, 0.14):
            cyl((x, y, 0.35), 0.025, 0.7, DWOOD, 8)
    cyl((0, 0, 0.82), 0.07, 0.2, GOLD, 12)
    cyl((0, 0, 1.0), 0.15, 0.22, GLOW, 16, r2=0.09)


# ------------------------------------------------------------------ Kitchen
@prop("Kitchen", "Fridge")
def _():
    box((0, 0, 0.85), (0.75, 0.7, 1.7), CREAM, 0.05)
    box((0, -0.355, 1.2), (0.7, 0.01, 0.02), DARK, 0.002)
    box((-0.3, -0.37, 1.35), (0.03, 0.05, 0.3), METAL, 0.005)
    box((-0.3, -0.37, 0.7), (0.03, 0.05, 0.5), METAL, 0.005)
    for x, z, c in ((0.15, 1.5, RED), (0.0, 1.4, BLUE), (0.2, 1.3, GREEN)):
        box((x, -0.36, z), (0.08, 0.02, 0.08), c, 0.005)
    box((0.1, -0.36, 1.5), (0.18, 0.005, 0.22), WHITE, 0.002, rot=(0, 0, 0.1))


@prop("Kitchen", "CounterSink")
def _():
    box((0, 0, 0.42), (1.4, 0.6, 0.84), WOOD, 0.02)
    box((0, 0, 0.86), (1.44, 0.64, 0.05), CREAM, 0.01)
    box((-0.25, -0.02, 0.87), (0.45, 0.4, 0.04), DARK, 0.01)
    cyl((-0.25, 0.22, 1.0), 0.02, 0.25, METAL, 8)
    box((-0.25, 0.1, 1.12), (0.03, 0.26, 0.03), METAL, 0.005)
    for x in (0.1, 0.4):
        box((x, -0.31, 0.5), (0.3, 0.02, 0.6), LWOOD, 0.005)
        box((x, -0.33, 0.65), (0.1, 0.02, 0.02), GOLD, 0.003)
    box((0.45, -0.1, 0.92), (0.3, 0.2, 0.03), LWOOD, 0.005)


@prop("Kitchen", "KitchenTable")
def _():
    cyl((0, 0, 0.74), 0.55, 0.05, WOOD, 24)
    cyl((0, 0, 0.37), 0.06, 0.72, DWOOD, 10)
    for a in (0, 2.1, 4.2):
        box((math.cos(a) * 0.2, math.sin(a) * 0.2, 0.04), (0.45, 0.06, 0.06), DWOOD, 0.01, rot=(0, 0, a))
    cyl((0, 0, 0.78), 0.2, 0.01, RED, 20)
    cyl((0.1, 0.05, 0.84), 0.06, 0.1, WHITE, 10)


@prop("Kitchen", "Chair")
def _():
    box((0, 0, 0.45), (0.42, 0.42, 0.05), WOOD, 0.01)
    box((0, 0.19, 0.78), (0.42, 0.04, 0.6), WOOD, 0.01)
    for x in (-0.18, 0.18):
        for y in (-0.18, 0.18):
            cyl((x, y, 0.22), 0.025, 0.44, DWOOD, 8)
    box((0, 0, 0.49), (0.36, 0.36, 0.04), RED, 0.015)


@prop("Kitchen", "CatBed")
def _():
    tor((0, 0, 0.1), 0.34, 0.1, CREAM, scale=(1, 1, 1))
    cyl((0, 0, 0.05), 0.33, 0.08, QUILT, 20)
    box((0.45, -0.1, 0.03), (0.2, 0.2, 0.06), METAL, 0.01)
    cyl((0.45, -0.1, 0.07), 0.08, 0.02, DARK, 12)
    sph((0.05, 0.05, 0.2), (0.15, 0.08, 0.06), ORANGE)


@prop("Kitchen", "HangingPots")
def _():
    box((0, 0, 0.5), (0.9, 0.04, 0.04), METAL, 0.005)
    for i, x in enumerate((-0.3, 0.0, 0.3)):
        cyl((x, 0, 0.35), 0.003, 0.2, METAL, 4)
        cyl((x, 0, 0.18), 0.1 + 0.02 * i, 0.1, METAL if i != 1 else RED, 14)
    cyl((0.0, 0, 0.18), 0.001, 0.001, METAL, 3)


# ------------------------------------------------------------------ Study
@prop("Study", "DeskComputer")
def _():
    box((0, 0, 0.74), (1.4, 0.7, 0.06), WOOD, 0.015)
    for x in (-0.62, 0.62):
        box((x, 0, 0.36), (0.08, 0.62, 0.72), DWOOD, 0.01)
    box((0.35, 0, 0.45), (0.5, 0.55, 0.5), WOOD, 0.01)
    box((-0.3, 0.1, 1.0), (0.5, 0.05, 0.4), DARK, 0.01)
    box((-0.3, 0.075, 1.0), (0.44, 0.01, 0.34), SCREEN, 0.003)
    box((-0.3, 0.1, 0.84), (0.1, 0.08, 0.15), DARK, 0.01)
    box((-0.3, -0.12, 0.78), (0.4, 0.14, 0.03), CREAM, 0.008)
    cyl((0.5, 0.2, 0.8), 0.06, 0.05, GOLD, 12)
    cyl((0.5, 0.2, 1.0), 0.12, 0.2, GLOW, 12, r2=0.05)
    for i in range(3):
        box((0.1 + 0.02 * i, -0.1, 0.78 + 0.012 * i), (0.22, 0.3, 0.01), WHITE, 0.002, rot=(0, 0, 0.1 * i))


@prop("Study", "LeatherChair")
def _():
    box((0, 0, 0.42), (0.7, 0.7, 0.2), LEATHER, 0.05)
    box((0, 0.3, 0.8), (0.7, 0.14, 0.7), LEATHER, 0.06)
    cyl((0, 0, 0.2), 0.05, 0.3, METAL, 8)
    for a in range(5):
        t = a * 1.2566
        box((math.cos(t) * 0.18, math.sin(t) * 0.18, 0.04), (0.38, 0.05, 0.05), METAL, 0.005, rot=(0, 0, t))


@prop("Study", "GrandpaBed")
def _():
    box((0, 0, 0.2), (1.0, 2.0, 0.3), DWOOD, 0.03)
    box((0, 0.99, 0.6), (1.0, 0.06, 0.8), DWOOD, 0.03)
    box((0, 0, 0.42), (0.92, 1.9, 0.18), CREAM, 0.05)
    box((0, -0.25, 0.54), (0.94, 1.2, 0.1), QUILT, 0.04)
    box((0, 0.7, 0.56), (0.7, 0.35, 0.12), WHITE, 0.05)


@prop("Study", "ReindeerSlippers")
def _():
    for x in (-0.07, 0.07):
        sph((x, 0, 0.05), (0.06, 0.13, 0.05), LEATHER)
        sph((x, -0.12, 0.09), (0.03, 0.03, 0.03), RED, 8, 6)
        box((x - 0.03, -0.04, 0.14), (0.02, 0.02, 0.07), DWOOD, 0.004)
        box((x + 0.03, -0.04, 0.14), (0.02, 0.02, 0.07), DWOOD, 0.004)


# ------------------------------------------------------------------ Garage
@prop("Garage", "CarUnderTarp")
def _():
    for x in (-0.8, 0.8):
        for y in (-1.3, 1.3):
            cyl((x, y, 0.3), 0.3, 0.2, DARK, 14, rot=(0, math.pi / 2, 0))
    box((0, 0, 0.62), (1.7, 4.0, 0.55), TARP, 0.15)
    box((0, 0.2, 1.05), (1.5, 2.0, 0.55), TARP, 0.2)
    sph((0.1, -0.4, 1.0), (0.5, 0.8, 0.3), TARP)


@prop("Garage", "GarageWorkbench")
def _():
    box((0, 0, 0.9), (1.6, 0.65, 0.07), LWOOD, 0.015)
    for x in (-0.72, 0.72):
        box((x, 0, 0.45), (0.1, 0.55, 0.9), DWOOD, 0.01)
    box((0, 0.2, 0.2), (1.4, 0.3, 0.04), DWOOD, 0.005)
    box((0, 0.31, 1.45), (1.5, 0.04, 0.9), WOOD, 0.01)
    for i, x in enumerate((-0.5, -0.1, 0.3, 0.6)):
        box((x, 0.27, 1.5), (0.04, 0.03, 0.35), METAL, 0.003)
        box((x, 0.27, 1.7), (0.1, 0.03, 0.06), RED if i % 2 else BLUE, 0.005)
    box((0.4, 0, 1.0), (0.2, 0.15, 0.12), RED, 0.01)
    cyl((-0.4, -0.05, 0.97), 0.06, 0.08, METAL, 12)


@prop("Garage", "ShelfWithBoxes")
def _():
    for x in (-0.55, 0.55):
        for y in (-0.2, 0.2):
            box((x, y, 1.0), (0.04, 0.04, 2.0), METAL, 0.003)
    for z in (0.3, 0.95, 1.6):
        box((0, 0, z), (1.15, 0.45, 0.03), METAL, 0.003)
    for i, (x, z, w) in enumerate(((-0.25, 0.42, 0.4), (0.25, 0.4, 0.35), (-0.2, 1.1, 0.5), (0.3, 1.07, 0.3), (0, 1.75, 0.45))):
        box((x, 0, z), (w, 0.35, 0.22 + 0.04 * (i % 2)), CARD, 0.015)
        box((x, -0.18, z), (w * 0.3, 0.005, 0.06), WHITE, 0.002)


@prop("Garage", "IcePuddle")
def _():
    sph((0, 0, 0.0), (0.9, 0.7, 0.03), ICE, 20, 8)
    sph((0.4, 0.2, 0.0), (0.45, 0.35, 0.025), ICE, 14, 6)


# ------------------------------------------------------------------ Hub
@prop("Hub", "Conveyor")
def _():
    box((0, 0, 0.75), (3.0, 0.8, 0.12), DARK, 0.02)
    box((0, -0.43, 0.7), (3.0, 0.06, 0.22), RED, 0.02)
    box((0, 0.43, 0.7), (3.0, 0.06, 0.22), RED, 0.02)
    for x in (-1.3, 0, 1.3):
        for y in (-0.38, 0.38):
            box((x, y, 0.35), (0.08, 0.08, 0.7), DWOOD, 0.01)
    for i in range(10):
        box((-1.35 + i * 0.3, 0, 0.82), (0.05, 0.7, 0.01), WHITE, 0.002)
    for x in (-1.5, 1.5):
        cyl((x, 0, 0.75), 0.08, 0.78, METAL, 12, rot=(math.pi / 2, 0, 0))


@prop("Hub", "OrderBoard")
def _():
    box((0, 0, 1.1), (1.6, 0.08, 1.1), DWOOD, 0.03)
    box((0, -0.03, 1.1), (1.42, 0.04, 0.92), CARD, 0.01)
    for x in (-0.7, 0.7):
        box((x, 0.0, 0.5), (0.08, 0.1, 1.0), DWOOD, 0.01)
    cols = [WHITE, CREAM, WHITE, QUILT, WHITE, CREAM]
    for i, c in enumerate(cols):
        x = -0.5 + (i % 3) * 0.5
        z = 1.3 - (i // 3) * 0.45
        box((x, -0.06, z), (0.32, 0.01, 0.38), c, 0.003, rot=(0, 0.05 * (i - 3), 0.05 * (i % 2 - 0.5)))
        sph((x, -0.075, z + 0.15), 0.02, RED, 6, 4)
    sph((0, -0.03, 1.75), (0.13, 0.04, 0.13), GOLD, 8, 6)


@prop("Hub", "Workbench")
def _():
    box((0, 0, 0.9), (1.8, 0.8, 0.08), LWOOD, 0.02)
    for x in (-0.8, 0.8):
        for y in (-0.3, 0.3):
            box((x, y, 0.45), (0.1, 0.1, 0.9), DWOOD, 0.01)
    box((0, 0.3, 0.3), (1.6, 0.35, 0.04), DWOOD, 0.005)
    box((-0.6, 0, 0.99), (0.22, 0.14, 0.1), METAL, 0.01)
    cyl((0.4, 0, 1.0), 0.12, 0.14, ORANGE, 12)
    cyl((0.4, 0, 1.15), 0.12, 0.14, ORANGE, 12)
    box((0.0, 0.37, 1.55), (1.7, 0.05, 1.0), WOOD, 0.01)
    box((0.0, 0.33, 1.6), (0.6, 0.02, 0.45), WHITE, 0.003)
    cyl((-0.5, -0.1, 1.0), 0.05, 0.12, METAL, 10)


@prop("Hub", "Sleigh")
def _():
    for x in (-0.45, 0.45):
        box((x, 0, 0.08), (0.06, 2.2, 0.06), GOLD, 0.01)
        tor((x, -1.1, 0.25), 0.18, 0.03, GOLD, rot=(0, math.pi / 2, 0))
        box((x, -0.6, 0.25), (0.04, 0.04, 0.3), GOLD, 0.005)
        box((x, 0.5, 0.25), (0.04, 0.04, 0.3), GOLD, 0.005)
    box((0, 0.1, 0.5), (1.0, 1.8, 0.45), RED, 0.1)
    box((0, 0.1, 0.65), (0.8, 1.6, 0.2), DARK, 0.04)
    box((0, 0.95, 0.9), (1.0, 0.18, 0.7), RED, 0.08)
    box((0, 0.1, 0.78), (0.9, 0.25, 0.06), CREAM, 0.02)


@prop("Hub", "GiftShelf")
def _():
    for x in (-0.6, 0.6):
        box((x, 0, 1.0), (0.06, 0.4, 2.0), DWOOD, 0.01)
    cols = [RED, GREEN, BLUE, GOLD]
    for j, z in enumerate((0.3, 0.95, 1.6)):
        box((0, 0, z), (1.3, 0.4, 0.04), WOOD, 0.005)
        x = -0.45
        for i in range(4):
            w = 0.18 + 0.05 * ((i + j) % 3)
            c = cols[(i + j) % 4]
            box((x + w / 2, 0, z + 0.14), (w, 0.3, 0.24), c, 0.01)
            box((x + w / 2, 0, z + 0.14), (0.03, 0.31, 0.25), WHITE, 0.003)
            x += w + 0.06


@prop("Decor", "Wreath")
def _():
    tor((0, 0, 0.35), 0.3, 0.07, GREEN, rot=(math.pi / 2, 0, 0))
    box((0, -0.08, 0.05), (0.12, 0.03, 0.1), RED, 0.005, rot=(0, 0, 0.5))
    box((0, -0.08, 0.05), (0.12, 0.03, 0.1), RED, 0.005, rot=(0, 0, -0.5))
    sph((0, -0.09, 0.07), 0.035, RED, 8, 6)
    for a in (0.5, 2.0, 3.6, 5.0):
        sph((math.cos(a) * 0.3, -0.06, 0.35 + math.sin(a) * 0.3), 0.025, RED, 6, 4)


@prop("Decor", "StringLights")
def _():
    for i in range(12):
        x = -0.9 + i * 0.165
        z = 0.1 - 0.06 * math.sin(math.pi * i / 11) * 2
        sph((x, 0, z), 0.03, [RED, GREEN, GOLD, BLUE][i % 4], 8, 6)
        box((x, 0, z + 0.03), (0.165, 0.008, 0.008), DARK, 0.001)


@prop("Decor", "WallClock")
def _():
    cyl((0, 0, 0.0), 0.22, 0.05, WOOD, 24, rot=(math.pi / 2, 0, 0))
    cyl((0, -0.03, 0.0), 0.18, 0.02, CREAM, 24, rot=(math.pi / 2, 0, 0))
    box((0, -0.045, 0.05), (0.012, 0.01, 0.1), DARK, 0.002)
    box((0.04, -0.045, 0.0), (0.08, 0.01, 0.012), DARK, 0.002)


# ------------------------------------------------------------------ build + export
def clear_parts():
    global parts
    parts = []


idx = 0
COLS = 8
for room, name, fn in PROPS:
    clear_parts()
    fn()
    bpy.ops.object.select_all(action="DESELECT")
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name
    o.data.name = name + "Mesh"
    out = os.path.join(ENV, room)
    os.makedirs(out, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + ".fbx"), use_selection=True,
                             object_types={"MESH"}, path_mode="COPY", embed_textures=False, bake_space_transform=True)
    o.location = ((idx % COLS) * 3.0, -(idx // COLS) * 3.5, 0)
    idx += 1

print("PROPS DONE:", len(PROPS))

# preview render
rows = (idx + COLS - 1) // COLS
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scene.collection.objects.link(cam)
cam.data.type = "ORTHO"
cam.data.ortho_scale = COLS * 3.0 * 0.95
cx = (COLS - 1) * 1.5
cy = -(rows - 1) * 1.75
cam.location = (cx, cy - 12, 8)
cam.rotation_euler = (math.radians(60), 0, 0)
scene.camera = cam
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
sun.rotation_euler = (0.9, 0.2, 0.5)
scene.collection.objects.link(sun)
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "MATERIAL"
scene.render.resolution_x = 2000
scene.render.resolution_y = int(2000 * rows * 3.5 / (COLS * 3.0) * 1.1)
scene.render.filepath = PREVIEW
bpy.ops.render.render(write_still=True)
