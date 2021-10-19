using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AssetStudio
{
	public sealed class SkinnedMeshRenderer : Renderer
	{
		public PPtr<Mesh> m_Mesh;
		public PPtr<Transform>[] m_Bones;
		public float[] m_BlendShapeWeights;

		public SkinnedMeshRenderer(ObjectReader reader) : base(reader)
		{
			int m_Quality = reader.ReadInt32();
			var m_UpdateWhenOffscreen = reader.ReadBoolean();
			if (version[0] >= 1 && version[1] >= 5 && version[0] <= 3 && version[1] <= 2) // 1.5 ~ 3.2
			{
				var m_SkinNormals = reader.ReadBoolean();
			}
			var m_SkinnedMotionVectors = reader.ReadBoolean(); reader.AlignStream();

			if (version[0] == 2 && version[1] < 6) //2.6 down
			{
				var m_DisableAnimationWhenOffscreen = new PPtr<Animation>(reader);
			}

			m_Mesh = new PPtr<Mesh>(reader);

			m_Bones = new PPtr<Transform>[reader.ReadInt32()];
			for (int b = 0; b < m_Bones.Length; b++)
			{
				m_Bones[b] = new PPtr<Transform>(reader);
			}

			if (version[0] > 4 || (version[0] == 4 && version[1] >= 3)) //4.3 and up
			{
				m_BlendShapeWeights = reader.ReadSingleArray();
			}

			var m_RootBone = new PPtr<Transform>(reader);
			var m_AABB = new AABB(reader);
			var m_DirtyAABB = reader.ReadBoolean();

			reader.AlignStream();
		}
	}
}
