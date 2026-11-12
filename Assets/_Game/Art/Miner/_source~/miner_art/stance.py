"""Render-only capture stances for the bible framings (mesh, rig rest pose and FBX are never touched).

A stance is a dict (stances.json, written by fit_stance.py):
  az, el                    orthographic view azimuth / elevation in degrees (0 = front, -90 = his right profile)
  upperarm.r ... lowerarm.l armature-space points the bone is aimed at (from its head), Idle frame 1 as the start
  pick_up                   armature-space direction the pick shaft is turned to (None: leave the hand alone)
  pick_roll                 degrees the hand turns about the shaft after that (faces the pick head to the camera)
  leg_spread                degrees each upper leg swings outward about the armature Y axis
  lean, twist               degrees the spine bends sideways (about Y) / turns (about Z); arms, head and crate follow
  head_yaw, head_tilt       degrees the head turns (about Z) / tilts sideways (about Y)
"""
import bpy
from mathutils import Matrix, Quaternion, Vector

ARM_BONES = ("upperarm.r", "lowerarm.r", "upperarm.l", "lowerarm.l")
IDLE_PICK_UP = Vector((-0.10, -0.06, 1.0)).normalized()     # build_miner.build_pick display-pose shaft direction


class Poser:
    def __init__(self, arm, scene):
        self.arm, self.scene = arm, scene
        arm.animation_data_create()
        arm.animation_data.action = bpy.data.actions["Idle"]
        scene.frame_set(1)
        self.idle = {pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones}
        arm.animation_data.action = None
        self.reset()

    def reset(self):
        for pb in self.arm.pose.bones:
            pb.matrix_basis = self.idle[pb.name]
        bpy.context.view_layer.update()

    def _rot_about(self, pb, q):
        head = pb.head.copy()
        pb.matrix = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ pb.matrix
        bpy.context.view_layer.update()

    def apply(self, st):
        self.reset()
        pbs = self.arm.pose.bones
        sp = st.get("leg_spread", 0.0)
        if sp:
            for nm, sgn in (("upperleg.l", 1), ("upperleg.r", -1)):
                self._rot_about(pbs[nm], Quaternion((0, 1, 0), -sgn * sp * 3.14159265 / 180.0))
        D = 3.14159265 / 180.0
        for bone, key, axis in (("spine", "lean", (0, 1, 0)), ("spine", "twist", (0, 0, 1)),
                                ("head", "head_yaw", (0, 0, 1)), ("head", "head_tilt", (0, 1, 0))):
            if st.get(key):
                self._rot_about(pbs[bone], Quaternion(axis, st[key] * D))
        slot_idle = pbs["handslot.r"].matrix.copy()
        for bone in ARM_BONES:
            if bone in st and st[bone] is not None:
                pb = pbs[bone]
                self._rot_about(pb, (pb.tail - pb.head).rotation_difference(Vector(st[bone]) - pb.head))
        if st.get("pick_up") is not None:
            slot_now = pbs["handslot.r"].matrix
            cur = ((slot_now @ slot_idle.inverted()).to_3x3() @ IDLE_PICK_UP).normalized()
            want = Vector(st["pick_up"]).normalized()
            self._rot_about(pbs["hand.r"], cur.rotation_difference(want))
            roll = st.get("pick_roll", 0.0)
            if roll:
                self._rot_about(pbs["hand.r"], Quaternion(want, roll * 3.14159265 / 180.0))
