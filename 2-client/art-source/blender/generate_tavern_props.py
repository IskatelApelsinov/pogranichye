#!/usr/bin/env python3
"""Generate original low-poly tavern props for Pogranichye in Blender 4.2+.

Run:
    blender --background --python generate_tavern_props.py

All geometry and 128 px colour textures are generated procedurally. No external
assets or add-ons are required. Outputs are written to ./generated/.
"""

import json
import math
import random
from pathlib import Path

import bpy
from mathutils import Vector


HERE = Path(__file__).resolve().parent
OUTPUT = HERE / "generated"
TEXTURE_DIR = OUTPUT / "textures"
TEXTURE_SIZE = 128
TRIANGLE_BUDGET = (300, 2000)
TAU = math.pi * 2.0


def clamp(value, low=0.0, high=1.0):
    return max(low, min(high, value))


def make_texture(name, base_rgb, seed, style):
    """Create a small deterministic, seamless-ish colour texture and pack it."""
    rng = random.Random(seed)
    image = bpy.data.images.new(name, width=TEXTURE_SIZE, height=TEXTURE_SIZE, alpha=True)
    pixels = []
    for y in range(TEXTURE_SIZE):
        v = y / float(TEXTURE_SIZE)
        for x in range(TEXTURE_SIZE):
            u = x / float(TEXTURE_SIZE)
            noise = rng.uniform(-0.035, 0.035)
            if style == "oak":
                grain = math.sin((v * 23.0 + math.sin(u * TAU * 3.0) * 0.35) * TAU)
                fine = math.sin((v * 67.0 + math.sin(u * TAU * 7.0) * 0.2) * TAU)
                factor = 1.0 + grain * 0.075 + fine * 0.025 + noise
            elif style == "wicker":
                fibre = math.sin((u * 18.0 + math.sin(v * TAU * 4.0) * 0.18) * TAU)
                factor = 1.0 + fibre * 0.07 + noise
            else:
                mottling = math.sin(u * TAU * 5.0) * math.sin(v * TAU * 4.0)
                factor = 1.0 + mottling * 0.06 + noise
            pixels.extend((
                clamp(base_rgb[0] * factor),
                clamp(base_rgb[1] * factor),
                clamp(base_rgb[2] * factor),
                1.0,
            ))
    image.pixels.foreach_set(pixels)
    image.filepath_raw = str(TEXTURE_DIR / (name.lower().replace(" ", "_") + ".png"))
    image.file_format = "PNG"
    image.save()
    image.pack()
    return image


def make_material(name, colour, roughness=0.7, metallic=0.0, image=None):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (colour[0], colour[1], colour[2], 1.0)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    shader.inputs["Base Color"].default_value = (colour[0], colour[1], colour[2], 1.0)
    shader.inputs["Roughness"].default_value = roughness
    shader.inputs["Metallic"].default_value = metallic
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    if image is not None:
        coordinates = nodes.new("ShaderNodeTexCoord")
        texture = nodes.new("ShaderNodeTexImage")
        texture.image = image
        texture.interpolation = "Linear"
        texture.extension = "REPEAT"
        links.new(coordinates.outputs["UV"], texture.inputs["Vector"])
        links.new(texture.outputs["Color"], shader.inputs["Base Color"])
    return material


def make_root(name):
    root = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(root)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.08
    root.location = (0.0, 0.0, 0.0)
    return root


def parent_to(obj, root):
    obj.parent = root
    obj.matrix_parent_inverse = root.matrix_world.inverted()
    return obj


def assign_material(obj, material):
    if obj.type == "MESH":
        obj.data.materials.append(material)
    elif obj.type == "CURVE":
        obj.data.materials.append(material)
    return obj


def activate(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def add_cube(root, name, location, dimensions, material, bevel=0.008):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    activate(obj)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0.0:
        modifier = obj.modifiers.new("Small worn edges", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
        modifier.profile = 0.5
        activate(obj)
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    assign_material(obj, material)
    return parent_to(obj, root)


def add_cylinder(root, name, location, radius, depth, material, vertices=16):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth, end_fill_type="NGON", location=location
    )
    obj = bpy.context.object
    obj.name = name
    assign_material(obj, material)
    return parent_to(obj, root)


def add_cone(root, name, location, radius_bottom, radius_top, depth, material, vertices=16):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices,
        radius1=radius_bottom,
        radius2=radius_top,
        depth=depth,
        end_fill_type="NOTHING",
        location=location,
    )
    obj = bpy.context.object
    obj.name = name
    assign_material(obj, material)
    return parent_to(obj, root)


def add_sphere(root, name, location, scale, material, segments=12, rings=8):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, radius=1.0, location=location
    )
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    activate(obj)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    assign_material(obj, material)
    return parent_to(obj, root)


def add_torus(root, name, location, major_radius, minor_radius, material, major_segments=12, minor_segments=4):
    bpy.ops.mesh.primitive_torus_add(
        major_segments=major_segments,
        minor_segments=minor_segments,
        major_radius=major_radius,
        minor_radius=minor_radius,
        location=location,
    )
    obj = bpy.context.object
    obj.name = name
    assign_material(obj, material)
    return parent_to(obj, root)


def add_curve(root, name, points, radius, material, cyclic=False):
    curve = bpy.data.curves.new(name, type="CURVE")
    curve.dimensions = "3D"
    curve.resolution_u = 1
    curve.bevel_depth = radius
    curve.bevel_resolution = 1
    curve.use_fill_caps = True
    spline = curve.splines.new("POLY")
    spline.points.add(len(points) - 1)
    for point, co in zip(spline.points, points):
        point.co = (co[0], co[1], co[2], 1.0)
    spline.use_cyclic_u = cyclic
    obj = bpy.data.objects.new(name, curve)
    bpy.context.scene.collection.objects.link(obj)
    assign_material(obj, material)
    return parent_to(obj, root)


def add_lathe(root, name, profile, segments, material):
    """Build a low-poly ring surface with continuous cylindrical UVs."""
    vertices = []
    faces = []
    uv_faces = []
    for ring_index, (radius, height) in enumerate(profile):
        for side in range(segments):
            angle = TAU * side / segments
            vertices.append((math.cos(angle) * radius, math.sin(angle) * radius, height))
    for ring_index in range(len(profile) - 1):
        v0 = ring_index / float(len(profile) - 1)
        v1 = (ring_index + 1) / float(len(profile) - 1)
        for side in range(segments):
            nxt = (side + 1) % segments
            faces.append((
                ring_index * segments + side,
                ring_index * segments + nxt,
                (ring_index + 1) * segments + nxt,
                (ring_index + 1) * segments + side,
            ))
            u0 = side / float(segments)
            u1 = (side + 1) / float(segments)
            uv_faces.append(((u0, v0), (u1, v0), (u1, v1), (u0, v1)))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for polygon, uv_coords in zip(mesh.polygons, uv_faces):
        polygon.use_smooth = True
        for loop_index, uv in zip(polygon.loop_indices, uv_coords):
            uv_layer.data[loop_index].uv = uv
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    assign_material(obj, material)
    return parent_to(obj, root)


def add_ring_curve(root, name, radius, height, tube_radius, material, points=24, wave=0.0, phase=0.0):
    path = []
    for index in range(points):
        angle = TAU * index / points
        r = radius + wave * math.sin(angle * 8.0 + phase)
        path.append((math.cos(angle) * r, math.sin(angle) * r, height + wave * 0.25 * math.cos(angle * 8.0 + phase)))
    return add_curve(root, name, path, tube_radius, material, cyclic=True)


def build_tankard(materials):
    root = make_root("PROP_Tankard_OakBeer")
    wood = materials["oak"]
    add_lathe(root, "Tankard • tapered oak wall",
              [(0.112, 0.012), (0.119, 0.028), (0.132, 0.265)], 16, wood)
    add_cylinder(root, "Tankard • bottom", (0.0, 0.0, 0.014), 0.113, 0.018, wood, 16)
    add_cylinder(root, "Tankard • amber ale", (0.0, 0.0, 0.244), 0.116, 0.008, materials["ale"], 16)
    add_torus(root, "Tankard • lower iron hoop", (0.0, 0.0, 0.058), 0.124, 0.008, materials["iron"])
    add_torus(root, "Tankard • upper iron hoop", (0.0, 0.0, 0.218), 0.133, 0.008, materials["iron"])
    handle_points = [
        (0.125, 0.0, 0.214), (0.176, 0.0, 0.214), (0.208, 0.0, 0.185),
        (0.208, 0.0, 0.105), (0.178, 0.0, 0.076), (0.125, 0.0, 0.076),
    ]
    add_curve(root, "Tankard • curved oak handle", handle_points, 0.014, wood)
    for side in range(12):
        angle = TAU * side / 12.0
        points = []
        for height, radius in ((0.035, 0.121), (0.12, 0.126), (0.245, 0.132)):
            points.append((math.cos(angle) * radius, math.sin(angle) * radius, height))
        add_curve(root, "Tankard • stave seam %02d" % side, points, 0.0018, materials["oak_shadow"])
    for index in range(5):
        angle = TAU * index / 5.0
        x = math.cos(angle) * 0.045
        y = math.sin(angle) * 0.045
        add_sphere(root, "Tankard • foam crown %02d" % index,
                   (x, y, 0.253 + (index % 2) * 0.003), (0.043, 0.039, 0.018), materials["foam"], 10, 6)
    return root


def build_barrel(materials):
    root = make_root("PROP_Barrel_Oak")
    add_lathe(root, "Barrel • bulged stave shell",
              [(0.164, 0.012), (0.188, 0.045), (0.218, 0.185), (0.188, 0.325), (0.164, 0.36)],
              16, materials["oak"])
    add_cylinder(root, "Barrel • lower head", (0.0, 0.0, 0.018), 0.164, 0.022, materials["oak"], 16)
    add_cylinder(root, "Barrel • top head", (0.0, 0.0, 0.354), 0.164, 0.018, materials["oak"], 16)
    for index, (height, radius) in enumerate(((0.065, 0.194), (0.185, 0.219), (0.305, 0.194))):
        add_torus(root, "Barrel • forged hoop %02d" % index,
                  (0.0, 0.0, height), radius, 0.009, materials["iron"])
    for side in range(12):
        angle = TAU * side / 12.0
        points = [
            (math.cos(angle) * 0.177, math.sin(angle) * 0.177, 0.035),
            (math.cos(angle) * 0.204, math.sin(angle) * 0.204, 0.12),
            (math.cos(angle) * 0.218, math.sin(angle) * 0.218, 0.185),
            (math.cos(angle) * 0.204, math.sin(angle) * 0.204, 0.25),
            (math.cos(angle) * 0.177, math.sin(angle) * 0.177, 0.337),
        ]
        add_curve(root, "Barrel • stave seam %02d" % side, points, 0.0018, materials["oak_shadow"])
    return root


def build_basket(materials):
    root = make_root("PROP_Basket_WickerMarket")
    add_lathe(root, "Basket • tapered woven base",
              [(0.105, 0.012), (0.112, 0.035), (0.165, 0.175), (0.178, 0.205)],
              12, materials["wicker"])
    add_cylinder(root, "Basket • woven bottom", (0.0, 0.0, 0.015), 0.106, 0.018, materials["wicker"], 12)
    for side in range(10):
        angle = TAU * side / 10.0
        points = []
        for height in (0.025, 0.09, 0.15, 0.205):
            radius = 0.108 + (height / 0.205) * 0.069
            offset = 0.003 * math.sin(height * 34.0 + side * math.pi)
            points.append((math.cos(angle) * (radius + offset), math.sin(angle) * (radius + offset), height))
        add_curve(root, "Basket • upright reed %02d" % side, points, 0.0045, materials["wicker_light"])
    for course, height in enumerate((0.055, 0.105, 0.155)):
        radius = 0.112 + (height / 0.205) * 0.066
        add_ring_curve(root, "Basket • woven course %02d" % course,
                       radius, height, 0.0055, materials["wicker_light"], points=24,
                       wave=0.006, phase=course * 0.9)
    add_ring_curve(root, "Basket • reinforced rim", 0.179, 0.205, 0.009, materials["wicker"], points=24)
    add_sphere(root, "Basket • country loaf", (0.0, -0.012, 0.247),
               (0.105, 0.062, 0.052), materials["bread"], 12, 8)
    add_curve(root, "Bread • scoring one", [(-0.052, -0.018, 0.285), (0.0, -0.018, 0.291), (0.052, -0.018, 0.285)],
              0.0025, materials["bread_score"])
    add_curve(root, "Bread • scoring two", [(-0.035, 0.018, 0.289), (0.012, 0.018, 0.292), (0.058, 0.018, 0.284)],
              0.0025, materials["bread_score"])
    add_sphere(root, "Basket • red apple", (-0.075, 0.065, 0.245),
               (0.043, 0.043, 0.044), materials["apple_red"], 10, 7)
    add_sphere(root, "Basket • green apple", (0.075, 0.055, 0.245),
               (0.043, 0.043, 0.044), materials["apple_green"], 10, 7)
    add_cylinder(root, "Apple • stem red", (-0.075, 0.065, 0.289), 0.004, 0.018, materials["oak_shadow"], 6)
    add_cylinder(root, "Apple • stem green", (0.075, 0.055, 0.289), 0.004, 0.018, materials["oak_shadow"], 6)
    return root


def build_bench(materials):
    root = make_root("PROP_Bench_Rustic")
    wood = materials["oak"]
    # Seat planks run along local X; the pivot remains at the centre of the ground footprint.
    for index, y in enumerate((-0.12, 0.0, 0.12)):
        add_cube(root, "Bench • seat plank %02d" % index,
                 (0.0, y, 0.435), (1.24, 0.105, 0.065), wood, 0.012)
    for x in (-0.49, 0.49):
        for y in (-0.145, 0.145):
            add_cube(root, "Bench • leg", (x, y, 0.205), (0.085, 0.085, 0.41), wood, 0.008)
    add_cube(root, "Bench • front stretcher", (0.0, -0.145, 0.19), (0.98, 0.055, 0.075), materials["oak_shadow"], 0.006)
    add_cube(root, "Bench • rear stretcher", (0.0, 0.145, 0.19), (0.98, 0.055, 0.075), materials["oak_shadow"], 0.006)
    add_cube(root, "Bench • left end brace", (-0.49, 0.0, 0.22), (0.055, 0.26, 0.075), materials["oak_shadow"], 0.006)
    add_cube(root, "Bench • right end brace", (0.49, 0.0, 0.22), (0.055, 0.26, 0.075), materials["oak_shadow"], 0.006)
    return root


def convert_curves_to_mesh():
    curves = [obj for obj in bpy.context.scene.objects if obj.type == "CURVE"]
    for obj in curves:
        activate(obj)
        bpy.ops.object.convert(target="MESH")


def triangle_count(root):
    count = 0
    for obj in root.children_recursive:
        if obj.type == "MESH":
            obj.data.calc_loop_triangles()
            count += len(obj.data.loop_triangles)
    return count


def selected_hierarchy(root):
    bpy.ops.object.select_all(action="DESELECT")
    objects = [root] + list(root.children_recursive)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root


def export_prop(root, output_dir):
    name = root.name.replace("PROP_", "").lower()
    selected_hierarchy(root)
    exported = []
    failures = []

    def attempt(label, extension, operator, **kwargs):
        if operator is None:
            failures.append("%s: exporter is not available" % label)
            return
        filepath = output_dir / (name + extension)
        try:
            result = operator(filepath=str(filepath), **kwargs)
            if "FINISHED" not in result:
                raise RuntimeError("operator returned %r" % (result,))
            exported.append(label)
        except Exception as exc:
            failures.append("%s: %s: %s" % (label, type(exc).__name__, exc))
            print("[Pogranichye] WARNING: %s export failed for %s: %s" % (
                label, root.name, exc
            ))

    export_scene = bpy.ops.export_scene
    attempt(
        "fbx",
        ".fbx",
        getattr(export_scene, "fbx", None),
        use_selection=True,
        object_types={"MESH", "EMPTY"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        use_mesh_modifiers=True,
        bake_anim=False,
        path_mode="COPY",
        embed_textures=True,
    )
    attempt(
        "glb",
        ".glb",
        getattr(export_scene, "gltf", None),
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_texcoords=True,
        export_normals=True,
        export_materials="EXPORT",
        export_image_format="AUTO",
    )

    if not exported:
        raise RuntimeError(
            "No usable FBX or glTF export for %s. Attempts: %s" % (
                root.name, "; ".join(failures)
            )
        )
    return exported, failures


def preview_sheet(roots, materials):
    scene = bpy.context.scene
    original_locations = {root.name: root.location.copy() for root in roots}
    positions = (-1.35, -0.48, 0.42, 1.55)
    for root, x in zip(roots, positions):
        root.location.x = x

    bpy.ops.mesh.primitive_plane_add(size=7.0, location=(0.0, 0.0, -0.012))
    ground = bpy.context.object
    ground.name = "__preview_ground"
    ground.data.materials.append(materials["ground"])

    bpy.ops.object.camera_add(location=(3.25, -5.2, 3.25))
    camera = bpy.context.object
    camera.name = "__preview_camera"
    target = Vector((0.0, 0.0, 0.26))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.lens = 48
    scene.camera = camera

    bpy.ops.object.light_add(type="AREA", location=(-2.5, -3.0, 4.2))
    key = bpy.context.object
    key.name = "__preview_key"
    key.data.energy = 520
    key.data.shape = "DISK"
    key.data.size = 4.0

    bpy.ops.object.light_add(type="AREA", location=(2.7, 1.8, 3.2))
    fill = bpy.context.object
    fill.name = "__preview_fill"
    fill.data.energy = 260
    fill.data.size = 3.0

    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 1440
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(OUTPUT / "tavern_props_preview.png")
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    if background is not None:
        background.inputs["Color"].default_value = (0.055, 0.048, 0.040, 1.0)
        background.inputs["Strength"].default_value = 0.8
    bpy.ops.render.render(write_still=True)

    for root in roots:
        root.location = original_locations[root.name]
    for name in ("__preview_ground", "__preview_camera", "__preview_key", "__preview_fill"):
        obj = bpy.data.objects.get(name)
        if obj is not None:
            bpy.data.objects.remove(obj, do_unlink=True)
    scene.camera = None


def build():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    TEXTURE_DIR.mkdir(parents=True, exist_ok=True)

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0

    textures = {
        "oak": make_texture("Oak grain", (0.30, 0.155, 0.067), 101, "oak"),
        "wicker": make_texture("Wicker reed", (0.48, 0.285, 0.125), 202, "wicker"),
        "bread": make_texture("Bread crust", (0.49, 0.245, 0.075), 303, "bread"),
    }
    materials = {
        "oak": make_material("Oak • warm aged timber", (0.30, 0.155, 0.067), 0.72, image=textures["oak"]),
        "oak_shadow": make_material("Oak • dark seams", (0.105, 0.047, 0.021), 0.82),
        "iron": make_material("Forged iron hoops", (0.105, 0.12, 0.125), 0.42, metallic=0.72),
        "ale": make_material("Amber ale", (0.43, 0.16, 0.025), 0.24, metallic=0.0),
        "foam": make_material("Tavern foam", (0.91, 0.82, 0.60), 0.34),
        "wicker": make_material("Wicker • reed core", (0.48, 0.285, 0.125), 0.8, image=textures["wicker"]),
        "wicker_light": make_material("Wicker • pale weave", (0.62, 0.40, 0.20), 0.78),
        "bread": make_material("Bread • crust", (0.49, 0.245, 0.075), 0.88, image=textures["bread"]),
        "bread_score": make_material("Bread • pale scoring", (0.82, 0.62, 0.34), 0.9),
        "apple_red": make_material("Apple • russet red", (0.42, 0.075, 0.035), 0.34),
        "apple_green": make_material("Apple • orchard green", (0.23, 0.34, 0.075), 0.36),
        "ground": make_material("Preview • neutral earth", (0.19, 0.17, 0.13), 0.95),
    }

    roots = [
        build_tankard(materials),
        build_barrel(materials),
        build_basket(materials),
        build_bench(materials),
    ]
    convert_curves_to_mesh()

    manifest = {
        "generator": "generate_tavern_props.py",
        "blender_version": bpy.app.version_string,
        "units": "metres",
        "texture_size": [TEXTURE_SIZE, TEXTURE_SIZE],
        "assets": [],
    }
    for root in roots:
        triangles = triangle_count(root)
        manifest["assets"].append({
            "root": root.name,
            "triangle_count": triangles,
            "triangle_budget": list(TRIANGLE_BUDGET),
            "within_triangle_budget": TRIANGLE_BUDGET[0] <= triangles <= TRIANGLE_BUDGET[1],
            "origin": "ground-centred",
            "materials": sorted({slot.material.name for obj in root.children_recursive
                                 if obj.type == "MESH" for slot in obj.material_slots if slot.material}),
        })

    preview_sheet(roots, materials)
    for root, asset in zip(roots, manifest["assets"]):
        formats, warnings = export_prop(root, OUTPUT)
        asset["exports"] = formats
        asset["export_warnings"] = warnings

    (OUTPUT / "asset_manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = None
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT / "Pogranichye_Tavern_Props.blend"))

    for asset in manifest["assets"]:
        print("[Pogranichye] %s: %d triangles" % (asset["root"], asset["triangle_count"]))
        if not asset["within_triangle_budget"]:
            print("[Pogranichye] WARNING: %s is outside the %d-%d triangle target." % (
                asset["root"], TRIANGLE_BUDGET[0], TRIANGLE_BUDGET[1]))
    print("[Pogranichye] Outputs written to %s" % OUTPUT)


if __name__ == "__main__":
    build()
