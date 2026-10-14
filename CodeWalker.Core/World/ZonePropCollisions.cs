using CodeWalker.GameFiles;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeWalker.World
{
    public static class ZonePropCollisions
    {
        public sealed class Part { public Bounds Bounds; public Matrix Transform; }

        /// <summary>Pristine physical state only. Bone/default-pose transforms replace fragment child composite transforms.</summary>
        public static List<Part> GetParts(DrawableBase drawable)
        {
            var parts = new List<Part>(); var seen = new HashSet<Bounds>();
            void Add(Bounds bounds, Matrix parent, Matrix? overrideTransform = null)
            {
                if (bounds == null || !seen.Add(bounds)) return;
                var matrix = (overrideTransform ?? bounds.Transform) * parent;
                if (bounds is BoundComposite composite)
                    foreach (var child in composite.Children?.data_items ?? new Bounds[0]) Add(child, matrix);
                else parts.Add(new Part { Bounds = bounds, Transform = matrix });
            }
            if (drawable is Drawable d) Add(d.Bound, Matrix.Identity);
            if (drawable is FragDrawable f)
            {
                var fragment = f.OwnerFragment;
                var lod = fragment?.PhysicsLODGroup?.PhysicsLOD1;
                var physics = lod?.Archetype1?.Bound ?? lod?.Bound;
                if (physics is BoundComposite composite)
                {
                    seen.Add(physics);
                    var children = composite.Children?.data_items ?? new Bounds[0];
                    var physicalChildren = lod.Children?.data_items ?? new FragPhysTypeChild[0];
                    var skeleton = fragment.Drawable?.Skeleton;
                    var bones = skeleton?.Bones?.Items ?? new Bone[0];
                    for (int i = 0; i < children.Length; i++)
                    {
                        Matrix? local = null;
                        var pc = i < physicalChildren.Length ? physicalChildren[i] : null;
                        var boneIndex = pc == null ? -1 : Array.FindIndex(bones, b => b.Tag == pc.BoneTag);
                        if (boneIndex >= 0 && pc.Drawable1 != null)
                        {
                            var pose = Matrix.Invert(bones[boneIndex].BindTransformInv);
                            var poses = fragment.BoneTransforms?.Items;
                            if (boneIndex < (poses?.Length ?? 0))
                            {
                                var p = poses[boneIndex]; var a = p.Row1; var b = p.Row2; var c = p.Row3;
                                pose = new Matrix(a.X,b.X,c.X,0,a.Y,b.Y,c.Y,0,a.Z,b.Z,c.Z,0,a.W,b.W,c.W,1);
                            }
                            local = pc.Drawable1.FragMatrix.ToMatrix() * pose;
                        }
                        Add(children[i], composite.Transform, local);
                    }
                }
                else Add(physics, Matrix.Identity);
                Add(f.Bound, Matrix.Identity); // May alias the physics bound or one of its children.
            }
            return parts;
        }
    }
}
