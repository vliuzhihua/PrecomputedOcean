# coding=utf-8
import struct

import bpy
import bmesh
from bpy_extras.object_utils import AddObjectHelper

from bpy.props import (
    FloatProperty,
)

def readint(f, size):
    return struct.unpack("i", f.read(size))[0]

def readfloat(f, size):
    return struct.unpack("f", f.read(size))[0]

def add_box(width, height, depth):
    """
    This function takes inputs and returns vertex and face arrays.
    no actual mesh data creation is done here.
    """
    world_scale = 32.0
    verts = []
    faces = []
    norms = []
    with open(r'G:\result\PrecomputedOceanData.data', 'rb') as f:
        array_length = readint(f, 4)
        tex_size = readint(f, 4)
        print ("array_length", array_length)
        print ("tex_size", tex_size)

        displace_array = []
        for array_layer in range(array_length):
            displace_array.append([])
            for y in range(tex_size):
                for x in range(tex_size):
                    v0 = readfloat(f, 4)
                    v1 = readfloat(f, 4)
                    v2 = readfloat(f, 4)
                    v3 = readfloat(f, 4)
                    displace_array[array_layer].append((v0, v1, v2, v3))
        normal_array = []
        for array_layer in range(array_length):
            normal_array.append([])
            for y in range(tex_size):
                for x in range(tex_size):
                    v0 = readfloat(f, 4)
                    v1 = readfloat(f, 4)
                    v2 = readfloat(f, 4)
                    v3 = readfloat(f, 4)
                    normal_array[array_layer].append((v0, v1, v2, v3))

        for y in range(tex_size):
            for x in range(tex_size):
                idx = x + y * tex_size
                px = world_scale / (tex_size - 1) * x + displace_array[0][idx][0]
                py = world_scale / (tex_size - 1) * y + displace_array[0][idx][2]
                pz = displace_array[0][idx][1]
                verts.append((px, py, pz))

                nx = normal_array[0][idx][0]
                ny = normal_array[0][idx][2]
                nz = normal_array[0][idx][1]
                norms.append((nx, ny, nz))

        for y in range(tex_size - 1):
            for x in range(tex_size - 1):
                idx0 = y * tex_size + x
                idx1 = (y + 1) * tex_size + x + 1
                idx2 = (y + 1) * tex_size + x
                faces.append((idx0, idx1, idx2))

                idx0 = y * tex_size + x
                idx1 = y * tex_size + x + 1
                idx2 = (y + 1) * tex_size + x + 1
                faces.append((idx0, idx1, idx2))

    return verts, faces, norms


class AddBox(bpy.types.Operator, AddObjectHelper):
    """Add a simple box mesh"""
    bl_idname = "mesh.primitive_box_add"
    bl_label = "Add Box"
    bl_options = {'REGISTER', 'UNDO'}

    width: FloatProperty(
        name="Width",
        description="Box Width",
        min=0.01, max=100.0,
        default=1.0,
    )
    height: FloatProperty(
        name="Height",
        description="Box Height",
        min=0.01, max=100.0,
        default=1.0,
    )
    depth: FloatProperty(
        name="Depth",
        description="Box Depth",
        min=0.01, max=100.0,
        default=1.0,
    )

    def execute(self, context):

        verts, faces, norms = add_box(
            self.width,
            self.height,
            self.depth,
        )
        context = bpy.context
        scene = context.scene

        # for c in scene.collection.children:
        #     scene.collection.remove(c)
        # bpy.data.meshes.remove("Ocean")
        mesh = bpy.data.meshes.new("Ocean")

        bm = bmesh.new()

        for i in range(len(verts)):

            v = bm.verts.new(verts[i])
            v.normal = norms[i]

        bm.verts.ensure_lookup_table()
        for f_idx in faces:
            bm.faces.new([bm.verts[i] for i in f_idx])

        # for n in norms:
        #     bm.normal.new(n)

        bm.to_mesh(mesh)
        mesh.update()

        # add the mesh as an object into the scene with this utility module
        from bpy_extras import object_utils
        object_utils.object_data_add(context, mesh, operator=self)

        return {'FINISHED'}


def menu_func(self, context):
    self.layout.operator(AddBox.bl_idname, icon='MESH_CUBE')
    # self.layout.operator(AddBox.bl_idname, icon='MESH_CUBE')

# Register and add to the "add mesh" menu (required to use F3 search "Add Box" for quick access)
def register():
    bpy.utils.register_class(AddBox)
    # bpy.types.VIEW3D_MT_mesh_add.append(menu_func)


def unregister():
    bpy.utils.unregister_class(AddBox)
    bpy.types.VIEW3D_MT_mesh_add.remove(menu_func)

if __name__ == "__main__":
    register()

    # AddBox ab = new AddBox()
    # test call
    bpy.ops.mesh.primitive_box_add()