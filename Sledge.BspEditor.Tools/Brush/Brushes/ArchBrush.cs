using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Tools.Brush.Brushes.Controls;
using Sledge.Common;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Hooks;
using Sledge.Common.Translations;
using Sledge.DataStructures.Geometric;
using Plane = Sledge.DataStructures.Geometric.Plane;

namespace Sledge.BspEditor.Tools.Brush.Brushes
{
    [Export(typeof(IBrush))]
    [Export(typeof(IInitialiseHook))]
    [OrderHint("H")]
    [AutoTranslate]
    public class ArchBrush : IBrush, IInitialiseHook
    {
        private NumericControl _numSides;
        private NumericControl _wallWidth;
        private NumericControl _arc;
        private NumericControl _startAngle;
        private NumericControl _addHeight;
        private BooleanControl _curvedRamp;
        private NumericControl _tiltAngle;
        private BooleanControl _tiltInterp;
        private BooleanControl _outerOffset;
        private BooleanControl _innerOffset;
        private BooleanControl _fitSelection;

        private const decimal Atan2 = 63.4m;

        public string NumberOfSides { get; set; }
        public string WallWidth { get; set; }
        public string Arc { get; set; }
        public string StartAngle { get; set; }
        public string AddHeight { get; set; }
        public string CurvedRamp { get; set; }
        public string TiltAngle { get; set; }
        public string TiltInterpolation { get; set; }
        public string OuterOffset { get; set; }
        public string InnerOffset { get; set; }
        public string FitSelection { get; set; }

        public async Task OnInitialise()
        {
            _numSides = new NumericControl(this) { LabelText = NumberOfSides };
            _wallWidth = new NumericControl(this) { LabelText = WallWidth, Minimum = 1, Maximum = 1024, Value = 32, Precision = 1 };
            _arc = new NumericControl(this) { LabelText = Arc, Minimum = 1, Maximum = 360 * 4, Value = 360 };
            _startAngle = new NumericControl(this) { LabelText = StartAngle, Minimum = 0, Maximum = 359, Value = 0 };
            _addHeight = new NumericControl(this) { LabelText = AddHeight, Minimum = -1024, Maximum = 1024, Value = 0, Precision = 1 };
            _curvedRamp = new BooleanControl(this) { LabelText = CurvedRamp, Checked = false };
            _tiltAngle = new NumericControl(this) { LabelText = TiltAngle, Minimum = -Atan2, Maximum = Atan2, Value = 0, Enabled = false, Precision = 1 };
            _tiltInterp = new BooleanControl(this) { LabelText = TiltInterpolation, Checked = false, Enabled = false };
            _outerOffset = new BooleanControl(this) { LabelText = OuterOffset, Checked = false, Enabled = false };
            _innerOffset = new BooleanControl(this) { LabelText = InnerOffset, Checked = false, Enabled = false };
            _fitSelection = new BooleanControl(this) { LabelText = FitSelection, Checked = false };

            // The offset checkboxes only apply to curved ramps
            _curvedRamp.ValuesChanged += (s, b) => _tiltAngle.Enabled = _tiltInterp.Enabled = _outerOffset.Enabled = _innerOffset.Enabled = _curvedRamp.GetValue();
        }

        public string Name { get; set; } = "Arch";
        public bool CanRound => true;

        public IEnumerable<BrushControl> GetControls()
        {
            yield return _numSides;
            yield return _wallWidth;
            yield return _arc;
            yield return _startAngle;
            yield return _addHeight;
            yield return _curvedRamp;
            yield return _tiltAngle;
            yield return _tiltInterp;
            yield return _outerOffset;
            yield return _innerOffset;
            yield return _fitSelection;
        }

        private Solid MakeSolid(UniqueNumberGenerator generator, IEnumerable<Vector3[]> faces, string texture, Color col)
        {
            var solid = new Solid(generator.Next("MapObject"));
            solid.Data.Add(new ObjectColor(col));
            foreach (var arr in faces)
            {
                var face = new Face(generator.Next("Face"))
                {
                    Plane = new Plane(arr[0], arr[1], arr[2]),
                    Texture = { Name = texture }
                };
                face.Vertices.AddRange(arr);
                solid.Data.Add(face);
            }
            solid.DescendantsChanged();
            return solid;
        }

        /// <summary>
        /// Bounds of the unit arc measured clockwise from 12 o'clock, where a point is (sin t, cos t).
        /// The extremes lie at the start, at the end, or at a multiple of 90 degrees in between.
        /// </summary>
        private static (double MinX, double MaxX, double MinY, double MaxY) UnitArcBounds(double start, double sweep)
        {
            const double quarter = Math.PI / 2;
            var end = start + sweep;
            var angles = new List<double> { start, end };
            for (var k = Math.Ceiling(start / quarter); k * quarter <= end; k++) angles.Add(k * quarter);

            var xs = angles.Select(t => Math.Sin(t)).ToList();
            var ys = angles.Select(t => Math.Cos(t)).ToList();
            return (xs.Min(), xs.Max(), ys.Min(), ys.Max());
        }

        /// <summary>
        /// The arch is swept clockwise, which reverses the winding of every face. The faces are flipped so
        /// their normals still point out of the solid.
        /// </summary>
        private static IEnumerable<Vector3[]> ReverseWinding(IEnumerable<Vector3[]> faces)
        {
            return faces.Select(f => Enumerable.Reverse(f).ToArray()).ToList();
        }

        /// <summary>
        /// The convex solid through a set of points: every plane through three of them that has all the others
        /// on one side. Used for the pieces of a sloped wall, whose top corners can drop onto the bottom ones.
        /// </summary>
        private static IEnumerable<Vector3[]> ConvexFaces(IList<Vector3> points)
        {
            const float eps = 0.001f;
            var planes = new List<Plane>();
            for (var a = 0; a < points.Count; a++)
            {
                for (var b = a + 1; b < points.Count; b++)
                {
                    for (var c = b + 1; c < points.Count; c++)
                    {
                        var n = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                        if (n.Length() < 1e-6f) continue;
                        n = Vector3.Normalize(n);

                        var max = float.MinValue;
                        var min = float.MaxValue;
                        foreach (var p in points)
                        {
                            var s = Vector3.Dot(n, p - points[a]);
                            if (s > max) max = s;
                            if (s < min) min = s;
                        }
                        if (max > eps && min < -eps) continue;   // not a supporting plane
                        if (max > eps) n = -n;                    // face the normal outwards

                        var plane = new Plane(n, points[a]);
                        if (planes.Any(x => x.EquivalentTo(plane, 0.001f))) continue;
                        planes.Add(plane);
                    }
                }
            }
            return PlanePolyhedron.Build(planes).Polygons.Select(p => p.Vertices.ToArray());
        }

        public IEnumerable<IMapObject> Create(UniqueNumberGenerator generator, Box box, string texture, int roundDecimals)
        {
            var numSides = (int)_numSides.GetValue();
            if (numSides < 3) yield break;
            var wallWidth = (float) _wallWidth.GetValue();
            if (wallWidth < 1) yield break;
            var arc = (float) _arc.GetValue();
            if (arc < 1) yield break;
            var startAngle = (float) _startAngle.GetValue();
            if (startAngle < 0 || startAngle > 359) yield break;
            var addHeight = (float) _addHeight.GetValue();
            var curvedRamp = _curvedRamp.GetValue();
            var tiltAngle = curvedRamp ? (float) _tiltAngle.GetValue() : 0;
            if (Math.Abs(Math.Abs(tiltAngle % 180) - 90) < 0.001f) yield break;
            var tiltInterp = curvedRamp && _tiltInterp.GetValue();
            var slopeOuter = curvedRamp && _outerOffset.GetValue();
            var slopeInner = curvedRamp && _innerOffset.GetValue();
            // Both edges dropped onto the bottom would leave no wall at all
            if (slopeOuter && slopeInner) yield break;
            var fitSelection = _fitSelection.GetValue();
            
            // Very similar to the pipe brush, except with options for start angle, arc, height and tilt
            var width = box.Width;
            var length = box.Length;
            var height = box.Height;

            var start = (float) MathHelper.DegreesToRadians(startAngle);
            var tilt = (float) MathHelper.DegreesToRadians(tiltAngle);
            var angle = (float) MathHelper.DegreesToRadians(arc) / numSides;

            // Centre and radii of the outer edge. Fit Selection stretches the arc so its outer edge fills the box,
            // so a 90 degree arc fits the box exactly. Otherwise the arc is the full ellipse inscribed in the box.
            var centreX = box.Center.X;
            var centreY = box.Center.Y;
            var majorOut = width / 2;
            var minorOut = length / 2;
            if (fitSelection)
            {
                var bounds = UnitArcBounds(start, MathHelper.DegreesToRadians(arc));
                var spanX = bounds.MaxX - bounds.MinX;
                var spanY = bounds.MaxY - bounds.MinY;
                if (spanX > 1e-6 && spanY > 1e-6)
                {
                    majorOut = (float) (width / spanX);
                    minorOut = (float) (length / spanY);
                    centreX = box.Start.X - (float) bounds.MinX * majorOut;
                    centreY = box.Start.Y - (float) bounds.MinY * minorOut;
                }
            }
            var majorIn = majorOut - wallWidth;
            var minorIn = minorOut - wallWidth;

            // Calculate the coordinates of the inner and outer ellipses' points
            var outer = new Vector3[numSides + 1];
            var inner = new Vector3[numSides + 1];
            for (var i = 0; i < numSides + 1; i++)
            {
                var a = start + i * angle;
                var h = i * addHeight;
                var interp = tiltInterp ? (float) Math.Cos(Math.PI / numSides * (i - numSides / 2f)) : 1;
                var tiltHeight = wallWidth / 2 * interp * (float) Math.Tan(tilt);
                
                // Angles run clockwise from 12 o'clock
                var xval = centreX + majorOut * (float) Math.Sin(a);
                var yval = centreY + minorOut * (float) Math.Cos(a);
                var zval = box.Start.Z + (curvedRamp ? h + tiltHeight : 0);
                outer[i] = new Vector3(xval, yval, zval).Round(roundDecimals);

                xval = centreX + majorIn * (float) Math.Sin(a);
                yval = centreY + minorIn * (float) Math.Cos(a);
                zval = box.Start.Z + (curvedRamp ? h - tiltHeight : 0);
                inner[i] = new Vector3(xval, yval, zval).Round(roundDecimals);
            }

            // Create the solids
            var colour = Colour.GetRandomBrushColour();
            var z = new Vector3(0, 0, height).Round(roundDecimals);
            for (var i = 0; i < numSides; i++)
            {
                var faces = new List<Vector3[]>();

                // A sloped edge drops its top vertices onto its bottom vertices. Each half of the segment is then
                // the convex solid through its corners, which can be a tetrahedron or a wedge.
                if (slopeOuter || slopeInner)
                {
                    var outerTop0 = slopeOuter ? outer[i] : outer[i] + z;
                    var outerTop1 = slopeOuter ? outer[i + 1] : outer[i + 1] + z;
                    var innerTop0 = slopeInner ? inner[i] : inner[i] + z;
                    var innerTop1 = slopeInner ? inner[i + 1] : inner[i + 1] + z;

                    // Same split as the curved ramp below, so the sloped pieces meet the unsloped ones
                    IList<Vector3> first;
                    IList<Vector3> second;
                    if (addHeight >= 0)
                    {
                        first = new[] { outer[i], outer[i + 1], inner[i], outerTop0, outerTop1, innerTop0 };
                        second = new[] { inner[i + 1], inner[i], outer[i + 1], innerTop1, innerTop0, outerTop1 };
                    }
                    else
                    {
                        first = new[] { inner[i], outer[i], inner[i + 1], innerTop0, outerTop0, innerTop1 };
                        second = new[] { outer[i], outer[i + 1], inner[i + 1], outerTop0, outerTop1, innerTop1 };
                    }
                    yield return MakeSolid(generator, ConvexFaces(first), texture, colour);
                    yield return MakeSolid(generator, ConvexFaces(second), texture, colour);
                    continue;
                }

                // Since we are triangulating/splitting each arch segment, we need to generate 2 brushes per side
                if (curvedRamp)
                {
                    // The splitting orientation depends on the curving direction of the arch
                    if (addHeight >= 0)
                    {
                        faces.Add(new[] { outer[i],       outer[i] + z,   outer[i+1] + z, outer[i+1] });
                        faces.Add(new[] { outer[i+1],     outer[i+1] + z, inner[i] + z,   inner[i]   });
                        faces.Add(new[] { inner[i],       inner[i] + z,   outer[i] + z,   outer[i]   });
                        faces.Add(new[] { outer[i] + z,   inner[i] + z,   outer[i+1] + z  });
                        faces.Add(new[] { outer[i+1],     inner[i],       outer[i]        });
                    }
                    else
                    {
                        faces.Add(new[] { inner[i+1],     inner[i+1] + z, inner[i] + z,   inner[i]   });
                        faces.Add(new[] { outer[i],       outer[i] + z,   inner[i+1] + z, inner[i+1] });
                        faces.Add(new[] { inner[i],       inner[i] + z,   outer[i] + z,   outer[i]   });
                        faces.Add(new[] { inner[i+1] + z, outer[i] + z,   inner[i] + z    });
                        faces.Add(new[] { inner[i],       outer[i],       inner[i+1]      });
                    }
                    yield return MakeSolid(generator, ReverseWinding(faces), texture, colour);

                    faces.Clear();

                    if (addHeight >= 0)
                    {
                        faces.Add(new[] { inner[i+1],     inner[i+1] + z, inner[i] + z,   inner[i]   });
                        faces.Add(new[] { inner[i],       inner[i] + z,   outer[i+1] + z, outer[i+1] });
                        faces.Add(new[] { outer[i+1],     outer[i+1] + z, inner[i+1] + z, inner[i+1] });
                        faces.Add(new[] { inner[i+1] + z, outer[i+1] + z, inner[i] + z    });
                        faces.Add(new[] { inner[i],       outer[i+1],     inner[i+1]      });
                    }
                    else
                    {
                        faces.Add(new[] { outer[i],       outer[i] + z,   outer[i+1] + z, outer[i+1] });
                        faces.Add(new[] { inner[i+1],     inner[i+1] + z, outer[i] + z,   outer[i]   });
                        faces.Add(new[] { outer[i+1],     outer[i+1] + z, inner[i+1] + z, inner[i+1] });
                        faces.Add(new[] { outer[i] + z,   inner[i+1] + z, outer[i+1] + z  });
                        faces.Add(new[] { outer[i+1],     inner[i+1],     outer[i]        });
                    }
                    yield return MakeSolid(generator, ReverseWinding(faces), texture, colour);
                }
                else
                {
                    var h = i * addHeight * Vector3.UnitZ;
                    faces.Add(new[] { outer[i],       outer[i] + z,   outer[i+1] + z, outer[i+1]   }.Select(x => x + h).ToArray());
                    faces.Add(new[] { inner[i+1],     inner[i+1] + z, inner[i] + z,   inner[i]     }.Select(x => x + h).ToArray());
                    faces.Add(new[] { outer[i+1],     outer[i+1] + z, inner[i+1] + z, inner[i+1]   }.Select(x => x + h).ToArray());
                    faces.Add(new[] { inner[i],       inner[i] + z,   outer[i] + z,   outer[i]     }.Select(x => x + h).ToArray());
                    faces.Add(new[] { inner[i+1] + z, outer[i+1] + z, outer[i] + z,   inner[i] + z }.Select(x => x + h).ToArray());
                    faces.Add(new[] { inner[i],       outer[i],       outer[i+1],     inner[i+1]   }.Select(x => x + h).ToArray());
                    yield return MakeSolid(generator, ReverseWinding(faces), texture, colour);
                }
            }
        }
    }
}
