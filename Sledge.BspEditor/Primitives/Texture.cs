using System;
using System.Numerics;
using System.Runtime.Serialization;
using Sledge.DataStructures.Geometric;

namespace Sledge.BspEditor.Primitives
{
	[Serializable]
    public class Texture : ISerializable
    {
        public string Name { get; set; }

        public float Rotation { get; set; }

        private Vector3 _uAxis;
        public Vector3 UAxis
        {
            get => _uAxis;
            set => _uAxis = value.Normalise();
        }

        private Vector3 _vAxis;
        public Vector3 VAxis
        {
            get => _vAxis;
            set => _vAxis = value.Normalise();
        }

        /// <summary>
        /// The axes above are always unit length. Hammer (and the VMF / MAP formats) use axis vectors as written, so a vector
        /// that isn't unit length has its length kept here. Rendering only uses the unit axes and the scales below;
        /// the raw vector is <c>UAxis * ULength</c> and the raw scale is <c>XScale * ULength</c> (same for V).
        /// </summary>
        public float ULength { get; set; } = 1;
        public float VLength { get; set; } = 1;

        public float XShift { get; set; }
        public float XScale { get; set; }

        public float YShift { get; set; }
        public float YScale { get; set; }

        public float? LightmapScale { get; set; }


        public Texture()
        {
            Name = "";
            Rotation = 0;
            _uAxis = -Vector3.UnitZ;
            _vAxis = Vector3.UnitX;
            XShift = YShift = 0;
            XScale = YScale = 1;
        }

        protected Texture(SerializationInfo info, StreamingContext context)
        {
            Name = info.GetString("Name");
            Rotation = info.GetInt32("Rotation");
            _uAxis = (Vector3)info.GetValue("UAxis", typeof(Vector3));
            _vAxis = (Vector3)info.GetValue("VAxis", typeof(Vector3));
            XShift = info.GetSingle("XShift");
            XScale = info.GetSingle("XScale");
            YShift = info.GetSingle("YShift");
            YScale = info.GetSingle("YScale");
            ULength = GetOrDefault(info, "ULength", 1f);
            VLength = GetOrDefault(info, "VLength", 1f);
            var lightmap = info.GetValue("lightmapscale", typeof(float));
            if (lightmap != null)
                LightmapScale = (float)lightmap;
        }

        private static float GetOrDefault(SerializationInfo info, string name, float defaultValue)
        {
            foreach (SerializationEntry entry in info)
            {
                if (entry.Name == name) return Convert.ToSingle(entry.Value);
            }
            return defaultValue;
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("ULength", ULength);
            info.AddValue("VLength", VLength);
            info.AddValue("Name", Name);
            info.AddValue("Rotation", Rotation);
            info.AddValue("UAxis", _uAxis);
            info.AddValue("VAxis", _vAxis);
            info.AddValue("XShift", XShift);
            info.AddValue("XScale", XScale);
            info.AddValue("YShift", YShift);
            info.AddValue("YScale", YScale);
            if (LightmapScale != null)
                info.AddValue("lightmapscale", LightmapScale);
        }

        public Vector3 GetNormal()
        {
            return UAxis.Cross(VAxis).Normalise();
        }

        public void Unclone(Texture source)
        {
            Name = source.Name;
            Rotation = source.Rotation;
            UAxis = source.UAxis;
            VAxis = source.VAxis;
            ULength = source.ULength;
            VLength = source.VLength;
            XShift = source.XShift;
            XScale = source.XScale;
            YShift = source.YShift;
            YScale = source.YScale;
        }

        public Texture Clone()
        {
            return new Texture
            {
                Name = Name,
                Rotation = Rotation,
                UAxis = UAxis,
                VAxis = VAxis,
                ULength = ULength,
                VLength = VLength,
                XShift = XShift,
                XScale = XScale,
                YShift = YShift,
                YScale = YScale,
                LightmapScale = LightmapScale
            };
        }
        
        /// <summary>
        /// Transform this texture in a non-destructive way.
        /// All vertices in the texture must remain unchanged relative to each
        /// other for this transformation to be valid.
        /// </summary>
        /// <param name="matrix">The matrix to transform the texture by</param>
        public void TransformUniform(Matrix4x4 matrix)
        {
            #if DEBUG

            // Validate that the transformation is indeed uniform.
            // If it's not, bail out.

            var one = Vector3.Transform(Vector3.One, matrix);
            var two = Vector3.Transform(Vector3.One * 2, matrix);
            if (Math.Abs((two - one).Length() - Vector3.One.Length()) > 0.01f)
                throw new InvalidOperationException("Transform isn't uniform!");

            #endif

            // If the determinant isn't 1, then we can't transform safely. (-1 is okay too)
            if (Math.Abs(matrix.GetDeterminant()) - 1 > 0.0001f) return;

            var startU = Vector3.Transform(UAxis, matrix);
            var startV = Vector3.Transform(VAxis, matrix);

            var origin = Vector3.Transform(Vector3.Zero, matrix);
            UAxis = (startU - origin).Normalise();
            VAxis = (startV - origin).Normalise();

            if (Math.Abs(XScale) > 0.001f && Math.Abs(YScale) > 0.001f)
            {
                XShift -= (startU - UAxis).Dot(UAxis) / XScale;
                YShift -= (startV - VAxis).Dot(VAxis) / YScale;
            }
        }

        /// <summary>
        /// Rescale this texture based on the given transformation matrix.
        /// Only scale changes of the transformation are taken into account.
        /// </summary>
        /// <param name="matrix">The transformation matrix</param>
        public void TransformScale(Matrix4x4 matrix)
        {
            var startU = Vector3.Transform(UAxis, matrix);
            var startV = Vector3.Transform(VAxis, matrix);

            var zero = Vector3.Transform(Vector3.Zero, matrix);
            var deltaU = (startU - zero).Length();
            var deltaV = (startV - zero).Length();

            if (Math.Abs(deltaU) > 0.001f) XScale *= deltaU;
            if (Math.Abs(deltaV) > 0.001f) YScale *= deltaV;
        }

		/// <summary>
		/// Skew this texture.
		/// All vertices in the texture must remain unchanged relative to each
		/// other for this transformation to be valid.
		/// </summary>
		/// <param name="matrix">The matrix to transform the texture by</param>
		public void TransformSkew(Matrix4x4 matrix)
		{
            var transformU = Vector3.Transform(UAxis, matrix);
            var transformV = Vector3.Transform(VAxis, matrix);

			var deltaU = transformU - UAxis;
			var deltaV = transformV - VAxis;


			matrix = Matrix4x4.Transpose(matrix);

            Matrix4x4.Invert(matrix, out matrix);
            
			var transformedU = Vector3.Transform(UAxis, matrix);
			var transformedV = Vector3.Transform(VAxis, matrix);
			var transformedOrigin = Vector3.Transform(Vector3.Zero, matrix);


			UAxis = (transformedU - transformedOrigin).Normalise();
			VAxis = (transformedV - transformedOrigin).Normalise();

			var scaleU = (transformedU - transformedOrigin).Length();
			var scaleV = (transformedV - transformedOrigin).Length();

			if (Math.Abs(scaleU) > 0.001f)
				XScale += 1-scaleU;
			if (Math.Abs(scaleV) > 0.001f)
				YScale += 1-scaleV;

            //FIXME: Shifts are inconsistent, learn more math!
            XShift += deltaU.Dot(transformedU) + (deltaU.Dot(UAxis) / XScale);
            YShift += deltaV.Dot(transformedV) + (deltaV.Dot(VAxis) / YScale);
        }
    }
}