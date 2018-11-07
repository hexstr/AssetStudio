﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using SharpDX;

namespace AssetStudio
{
    public class Keyframe<T>
    {
        public float time { get; set; }
        public T value { get; set; }
        public T inSlope { get; set; }
        public T outSlope { get; set; }
        public int weightedMode { get; set; }
        public T inWeight { get; set; }
        public T outWeight { get; set; }


        public Keyframe(ObjectReader reader, Func<T> readerFunc)
        {
            time = reader.ReadSingle();
            value = readerFunc();
            inSlope = readerFunc();
            outSlope = readerFunc();
            if (reader.version[0] >= 2018)
            {
                weightedMode = reader.ReadInt32();
                inWeight = readerFunc();
                outWeight = readerFunc();
            }
        }
    }

    public class AnimationCurve<T>
    {
        public List<Keyframe<T>> m_Curve { get; set; }
        public int m_PreInfinity { get; set; }
        public int m_PostInfinity { get; set; }
        public int m_RotationOrder { get; set; }

        public AnimationCurve(ObjectReader reader, Func<T> readerFunc)
        {
            var version = reader.version;
            int numCurves = reader.ReadInt32();
            m_Curve = new List<Keyframe<T>>(numCurves);
            for (int i = 0; i < numCurves; i++)
            {
                m_Curve.Add(new Keyframe<T>(reader, readerFunc));
            }

            m_PreInfinity = reader.ReadInt32();
            m_PostInfinity = reader.ReadInt32();
            if (version[0] > 5 || (version[0] == 5 && version[1] >= 3))//5.3 and up
            {
                m_RotationOrder = reader.ReadInt32();
            }
        }
    }

    public class QuaternionCurve
    {
        public AnimationCurve<Quaternion> curve { get; set; }
        public string path { get; set; }

        public QuaternionCurve(ObjectReader reader)
        {
            curve = new AnimationCurve<Quaternion>(reader, reader.ReadQuaternion);
            path = reader.ReadAlignedString();
        }
    }

    public class PackedFloatVector
    {
        public uint m_NumItems { get; set; }
        public float m_Range { get; set; }
        public float m_Start { get; set; }
        public byte[] m_Data { get; set; }
        public byte m_BitSize { get; set; }

        public PackedFloatVector(ObjectReader reader)
        {
            m_NumItems = reader.ReadUInt32();
            m_Range = reader.ReadSingle();
            m_Start = reader.ReadSingle();

            int numData = reader.ReadInt32();
            m_Data = reader.ReadBytes(numData);
            reader.AlignStream(4);

            m_BitSize = reader.ReadByte();
            reader.AlignStream(4);
        }

        public float[] UnpackFloats(int itemCountInChunk, int chunkStride, int start = 0, int numChunks = -1)
        {
            int bitPos = m_BitSize * start;
            int indexPos = bitPos / 8;
            bitPos %= 8;

            float scale = 1.0f / m_Range;
            if (numChunks == -1)
                numChunks = (int)m_NumItems / itemCountInChunk;
            var end = chunkStride * numChunks / 4;
            var data = new List<float>();
            for (var index = 0; index != end; index += chunkStride / 4)
            {
                for (int i = 0; i < itemCountInChunk; ++i)
                {
                    uint x = 0;

                    int bits = 0;
                    while (bits < m_BitSize)
                    {
                        x |= (uint)((m_Data[indexPos] >> bitPos) << bits);
                        int num = Math.Min(m_BitSize - bits, 8 - bitPos);
                        bitPos += num;
                        bits += num;
                        if (bitPos == 8)
                        {
                            indexPos++;
                            bitPos = 0;
                        }
                    }
                    x &= (uint)(1 << m_BitSize) - 1u;
                    data.Add(x / (scale * ((1 << m_BitSize) - 1)) + m_Start);
                }
            }

            return data.ToArray();
        }
    }

    public class PackedIntVector
    {
        public uint m_NumItems { get; set; }
        public byte[] m_Data { get; set; }
        public byte m_BitSize { get; set; }

        public PackedIntVector(ObjectReader reader)
        {
            m_NumItems = reader.ReadUInt32();

            int numData = reader.ReadInt32();
            m_Data = reader.ReadBytes(numData);
            reader.AlignStream(4);

            m_BitSize = reader.ReadByte();
            reader.AlignStream(4);
        }

        public int[] UnpackInts()
        {
            var data = new int[m_NumItems];
            int indexPos = 0;
            int bitPos = 0;
            for (int i = 0; i < m_NumItems; i++)
            {
                int bits = 0;
                data[i] = 0;
                while (bits < m_BitSize)
                {
                    data[i] |= (m_Data[indexPos] >> bitPos) << bits;
                    int num = Math.Min(m_BitSize - bits, 8 - bitPos);
                    bitPos += num;
                    bits += num;
                    if (bitPos == 8)
                    {
                        indexPos++;
                        bitPos = 0;
                    }
                }
                data[i] &= (1 << m_BitSize) - 1;
            }
            return data;
        }
    }

    public class PackedQuatVector
    {
        public uint m_NumItems { get; set; }
        public byte[] m_Data { get; set; }

        public PackedQuatVector(ObjectReader reader)
        {
            m_NumItems = reader.ReadUInt32();

            int numData = reader.ReadInt32();
            m_Data = reader.ReadBytes(numData);

            reader.AlignStream(4);
        }

        public Quaternion[] UnpackQuats()
        {
            var data = new Quaternion[m_NumItems];
            int indexPos = 0;
            int bitPos = 0;

            for (int i = 0; i < m_NumItems; i++)
            {
                uint flags = 0;

                int bits = 0;
                while (bits < 3)
                {
                    flags |= (uint)((m_Data[indexPos] >> bitPos) << bits);
                    int num = Math.Min(3 - bits, 8 - bitPos);
                    bitPos += num;
                    bits += num;
                    if (bitPos == 8)
                    {
                        indexPos++;
                        bitPos = 0;
                    }
                }
                flags &= 7;


                var q = new Quaternion();
                float sum = 0;
                for (int j = 0; j < 4; j++)
                {
                    if ((flags & 3) != j)
                    {
                        int bitSize = ((flags & 3) + 1) % 4 == j ? 9 : 10;
                        uint x = 0;

                        bits = 0;
                        while (bits < bitSize)
                        {
                            x |= (uint)((m_Data[indexPos] >> bitPos) << bits);
                            int num = Math.Min(bitSize - bits, 8 - bitPos);
                            bitPos += num;
                            bits += num;
                            if (bitPos == 8)
                            {
                                indexPos++;
                                bitPos = 0;
                            }
                        }
                        x &= (uint)((1 << bitSize) - 1);
                        q[j] = x / (0.5f * ((1 << bitSize) - 1)) - 1;
                        sum += q[j] * q[j];
                    }
                }

                int lastComponent = (int)(flags & 3);
                q[lastComponent] = (float)Math.Sqrt(1 - sum);
                if ((flags & 4) != 0u)
                    q[lastComponent] = -q[lastComponent];
                data[i] = q;
            }

            return data;
        }
    }

    public class CompressedAnimationCurve
    {
        public string m_Path { get; set; }
        public PackedIntVector m_Times { get; set; }
        public PackedQuatVector m_Values { get; set; }
        public PackedFloatVector m_Slopes { get; set; }
        public int m_PreInfinity { get; set; }
        public int m_PostInfinity { get; set; }

        public CompressedAnimationCurve(ObjectReader reader)
        {
            m_Path = reader.ReadAlignedString();
            m_Times = new PackedIntVector(reader);
            m_Values = new PackedQuatVector(reader);
            m_Slopes = new PackedFloatVector(reader);
            m_PreInfinity = reader.ReadInt32();
            m_PostInfinity = reader.ReadInt32();
        }
    }

    public class Vector3Curve
    {
        public AnimationCurve<Vector3> curve { get; set; }
        public string path { get; set; }

        public Vector3Curve(ObjectReader reader)
        {
            curve = new AnimationCurve<Vector3>(reader, reader.ReadVector3);
            path = reader.ReadAlignedString();
        }
    }

    public class FloatCurve
    {
        public AnimationCurve<float> curve { get; set; }
        public string attribute { get; set; }
        public string path { get; set; }
        public int classID { get; set; }
        public PPtr script { get; set; }


        public FloatCurve(ObjectReader reader)
        {
            curve = new AnimationCurve<float>(reader, reader.ReadSingle);
            attribute = reader.ReadAlignedString();
            path = reader.ReadAlignedString();
            classID = reader.ReadInt32();
            script = reader.ReadPPtr();
        }
    }

    public class PPtrKeyframe
    {
        public float time { get; set; }
        public PPtr value { get; set; }


        public PPtrKeyframe(ObjectReader reader)
        {
            time = reader.ReadSingle();
            value = reader.ReadPPtr();
        }
    }

    public class PPtrCurve
    {
        public List<PPtrKeyframe> curve { get; set; }
        public string attribute { get; set; }
        public string path { get; set; }
        public int classID { get; set; }
        public PPtr script { get; set; }


        public PPtrCurve(ObjectReader reader)
        {
            int numCurves = reader.ReadInt32();
            curve = new List<PPtrKeyframe>(numCurves);
            for (int i = 0; i < numCurves; i++)
            {
                curve.Add(new PPtrKeyframe(reader));
            }

            attribute = reader.ReadAlignedString();
            path = reader.ReadAlignedString();
            classID = reader.ReadInt32();
            script = reader.ReadPPtr();
        }
    }

    public class AABB
    {
        public Vector3 m_Center { get; set; }
        public Vector3 m_Extend { get; set; }

        public AABB(ObjectReader reader)
        {
            m_Center = reader.ReadVector3();
            m_Extend = reader.ReadVector3();
        }
    }

    public class xform
    {
        public object t { get; set; }
        public Quaternion q { get; set; }
        public object s { get; set; }

        public xform(ObjectReader reader)
        {
            var version = reader.version;
            t = version[0] > 5 || (version[0] == 5 && version[1] >= 4) ? (object)reader.ReadVector3() : (object)reader.ReadVector4();//5.4 and up
            q = reader.ReadQuaternion();
            s = version[0] > 5 || (version[0] == 5 && version[1] >= 4) ? (object)reader.ReadVector3() : (object)reader.ReadVector4();//5.4 and up
        }
    }

    public class HandPose
    {
        public xform m_GrabX { get; set; }
        public float[] m_DoFArray { get; set; }
        public float m_Override { get; set; }
        public float m_CloseOpen { get; set; }
        public float m_InOut { get; set; }
        public float m_Grab { get; set; }

        public HandPose(ObjectReader reader)
        {
            m_GrabX = new xform(reader);

            int numDoFs = reader.ReadInt32();
            m_DoFArray = reader.ReadSingleArray(numDoFs);

            m_Override = reader.ReadSingle();
            m_CloseOpen = reader.ReadSingle();
            m_InOut = reader.ReadSingle();
            m_Grab = reader.ReadSingle();
        }
    }

    public class HumanGoal
    {
        public xform m_X { get; set; }
        public float m_WeightT { get; set; }
        public float m_WeightR { get; set; }
        public object m_HintT { get; set; }
        public float m_HintWeightT { get; set; }

        public HumanGoal(ObjectReader reader)
        {
            var version = reader.version;
            m_X = new xform(reader);
            m_WeightT = reader.ReadSingle();
            m_WeightR = reader.ReadSingle();
            if (version[0] >= 5)//5.0 and up
            {
                m_HintT = version[0] > 5 || (version[0] == 5 && version[1] >= 4) ? (object)reader.ReadVector3() : (object)reader.ReadVector4();//5.4 and up
                m_HintWeightT = reader.ReadSingle();
            }
        }
    }

    public class HumanPose
    {
        public xform m_RootX { get; set; }
        public object m_LookAtPosition { get; set; }
        public Vector4 m_LookAtWeight { get; set; }
        public List<HumanGoal> m_GoalArray { get; set; }
        public HandPose m_LeftHandPose { get; set; }
        public HandPose m_RightHandPose { get; set; }
        public float[] m_DoFArray { get; set; }
        public object[] m_TDoFArray { get; set; }

        public HumanPose(ObjectReader reader)
        {
            var version = reader.version;
            m_RootX = new xform(reader);
            m_LookAtPosition = version[0] > 5 || (version[0] == 5 && version[1] >= 4) ? (object)reader.ReadVector3() : (object)reader.ReadVector4();//5.4 and up
            m_LookAtWeight = reader.ReadVector4();

            int numGoals = reader.ReadInt32();
            m_GoalArray = new List<HumanGoal>(numGoals);
            for (int i = 0; i < numGoals; i++)
            {
                m_GoalArray.Add(new HumanGoal(reader));
            }

            m_LeftHandPose = new HandPose(reader);
            m_RightHandPose = new HandPose(reader);

            int numDoFs = reader.ReadInt32();
            m_DoFArray = reader.ReadSingleArray(numDoFs);

            if (version[0] > 5 || (version[0] == 5 && version[1] >= 2))//5.2 and up
            {
                int numTDof = reader.ReadInt32();
                m_TDoFArray = new object[numTDof];
                for (int i = 0; i < numTDof; i++)
                {
                    m_TDoFArray[i] = version[0] > 5 || (version[0] == 5 && version[1] >= 4) ? (object)reader.ReadVector3() : (object)reader.ReadVector4();//5.4 and up
                }
            }
        }
    }

    public class StreamedClip
    {
        public uint[] data { get; set; }
        public uint curveCount { get; set; }

        public StreamedClip(ObjectReader reader)
        {
            int numData = reader.ReadInt32();
            data = reader.ReadUInt32Array(numData);
            curveCount = reader.ReadUInt32();
        }

        public class StreamedCurveKey
        {
            public int index { get; set; }
            public float[] coeff { get; set; }

            public float value;
            public float outSlope;
            public float inSlope;

            public StreamedCurveKey(BinaryReader reader)
            {
                index = reader.ReadInt32();
                coeff = reader.ReadSingleArray(4);

                outSlope = coeff[2];
                value = coeff[3];
            }

            public float CalculateNextInSlope(float dx, StreamedCurveKey rhs)
            {
                //Stepped
                if (coeff[0] == 0f && coeff[1] == 0f && coeff[2] == 0f)
                {
                    return float.PositiveInfinity;
                }

                dx = Math.Max(dx, 0.0001f);
                var dy = rhs.value - value;
                var length = 1.0f / (dx * dx);
                var d1 = outSlope * dx;
                var d2 = dy + dy + dy - d1 - d1 - coeff[1] / length;
                return d2 / dx;
            }
        }

        public class StreamedFrame
        {
            public float time { get; set; }
            public List<StreamedCurveKey> keyList { get; set; }

            public StreamedFrame(BinaryReader reader)
            {
                time = reader.ReadSingle();

                int numKeys = reader.ReadInt32();
                keyList = new List<StreamedCurveKey>(numKeys);
                for (int i = 0; i < numKeys; i++)
                {
                    keyList.Add(new StreamedCurveKey(reader));
                }
            }
        }

        public List<StreamedFrame> ReadData()
        {
            var frameList = new List<StreamedFrame>();
            using (Stream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(data);
                stream.Position = 0;
                while (stream.Position < stream.Length)
                {
                    frameList.Add(new StreamedFrame(new BinaryReader(stream)));
                }
            }

            for (int frameIndex = 2; frameIndex < frameList.Count - 1; frameIndex++)
            {
                var frame = frameList[frameIndex];
                foreach (var curveKey in frame.keyList)
                {
                    for (int i = frameIndex - 1; i >= 0; i--)
                    {
                        var preFrame = frameList[i];
                        var preCurveKey = preFrame.keyList.Find(x => x.index == curveKey.index);
                        if (preCurveKey != null)
                        {
                            curveKey.inSlope = preCurveKey.CalculateNextInSlope(frame.time - preFrame.time, curveKey);
                            break;
                        }
                    }
                }
            }
            return frameList;
        }
    }

    public class DenseClip
    {
        public int m_FrameCount { get; set; }
        public uint m_CurveCount { get; set; }
        public float m_SampleRate { get; set; }
        public float m_BeginTime { get; set; }
        public float[] m_SampleArray { get; set; }

        public DenseClip(ObjectReader reader)
        {
            m_FrameCount = reader.ReadInt32();
            m_CurveCount = reader.ReadUInt32();
            m_SampleRate = reader.ReadSingle();
            m_BeginTime = reader.ReadSingle();

            int numSamples = reader.ReadInt32();
            m_SampleArray = reader.ReadSingleArray(numSamples);
        }
    }

    public class ConstantClip
    {
        public float[] data { get; set; }

        public ConstantClip(ObjectReader reader)
        {
            int numData = reader.ReadInt32();
            data = reader.ReadSingleArray(numData);
        }
    }

    public class ValueConstant
    {
        public uint m_ID { get; set; }
        public uint m_TypeID { get; set; }
        public uint m_Type { get; set; }
        public uint m_Index { get; set; }

        public ValueConstant(ObjectReader reader)
        {
            var version = reader.version;
            m_ID = reader.ReadUInt32();
            if (version[0] < 5 || (version[0] == 5 && version[1] < 5))//5.5 down
            {
                m_TypeID = reader.ReadUInt32();
            }
            m_Type = reader.ReadUInt32();
            m_Index = reader.ReadUInt32();
        }
    }

    public class ValueArrayConstant
    {
        public List<ValueConstant> m_ValueArray { get; set; }

        public ValueArrayConstant(ObjectReader reader)
        {
            int numVals = reader.ReadInt32();
            m_ValueArray = new List<ValueConstant>(numVals);
            for (int i = 0; i < numVals; i++)
            {
                m_ValueArray.Add(new ValueConstant(reader));
            }
        }
    }

    public class Clip
    {
        public StreamedClip m_StreamedClip { get; set; }
        public DenseClip m_DenseClip { get; set; }
        public ConstantClip m_ConstantClip { get; set; }
        public ValueArrayConstant m_Binding { get; set; }

        public Clip(ObjectReader reader)
        {
            var version = reader.version;
            m_StreamedClip = new StreamedClip(reader);
            m_DenseClip = new DenseClip(reader);
            if (version[0] > 4 || (version[0] == 4 && version[1] >= 3)) //4.3 and up
            {
                m_ConstantClip = new ConstantClip(reader);
            }
            m_Binding = new ValueArrayConstant(reader);
        }
    }

    public class ValueDelta
    {
        public float m_Start { get; set; }
        public float m_Stop { get; set; }

        public ValueDelta(ObjectReader reader)
        {
            m_Start = reader.ReadSingle();
            m_Stop = reader.ReadSingle();
        }
    }

    public class ClipMuscleConstant
    {
        public HumanPose m_DeltaPose { get; set; }
        public xform m_StartX { get; set; }
        public xform m_StopX { get; set; }
        public xform m_LeftFootStartX { get; set; }
        public xform m_RightFootStartX { get; set; }
        public xform m_MotionStartX { get; set; }
        public xform m_MotionStopX { get; set; }
        public object m_AverageSpeed { get; set; }
        public Clip m_Clip { get; set; }
        public float m_StartTime { get; set; }
        public float m_StopTime { get; set; }
        public float m_OrientationOffsetY { get; set; }
        public float m_Level { get; set; }
        public float m_CycleOffset { get; set; }
        public float m_AverageAngularSpeed { get; set; }
        public int[] m_IndexArray { get; set; }
        public List<ValueDelta> m_ValueArrayDelta { get; set; }
        public float[] m_ValueArrayReferencePose { get; set; }
        public bool m_Mirror { get; set; }
        public bool m_LoopTime { get; set; }
        public bool m_LoopBlend { get; set; }
        public bool m_LoopBlendOrientation { get; set; }
        public bool m_LoopBlendPositionY { get; set; }
        public bool m_LoopBlendPositionXZ { get; set; }
        public bool m_StartAtOrigin { get; set; }
        public bool m_KeepOriginalOrientation { get; set; }
        public bool m_KeepOriginalPositionY { get; set; }
        public bool m_KeepOriginalPositionXZ { get; set; }
        public bool m_HeightFromFeet { get; set; }

        public ClipMuscleConstant(ObjectReader reader)
        {
            var version = reader.version;
            m_DeltaPose = new HumanPose(reader);
            m_StartX = new xform(reader);
            if (version[0] > 5 || (version[0] == 5 && version[1] >= 5))//5.5 and up
            {
                m_StopX = new xform(reader);
            }
            m_LeftFootStartX = new xform(reader);
            m_RightFootStartX = new xform(reader);
            if (version[0] < 5)//5.0 down
            {
                m_MotionStartX = new xform(reader);
                m_MotionStopX = new xform(reader);
            }
            m_AverageSpeed = version[0] > 5 || (version[0] == 5 && version[1] >= 4) ? (object)reader.ReadVector3() : (object)reader.ReadVector4();//5.4 and up
            m_Clip = new Clip(reader);
            m_StartTime = reader.ReadSingle();
            m_StopTime = reader.ReadSingle();
            m_OrientationOffsetY = reader.ReadSingle();
            m_Level = reader.ReadSingle();
            m_CycleOffset = reader.ReadSingle();
            m_AverageAngularSpeed = reader.ReadSingle();

            int numIndices = reader.ReadInt32();
            m_IndexArray = reader.ReadInt32Array(numIndices);
            if (version[0] < 4 || (version[0] == 4 && version[1] < 3)) //4.3 down
            {
                int numAdditionalCurveIndexs = reader.ReadInt32();
                var m_AdditionalCurveIndexArray = new List<int>(numAdditionalCurveIndexs);
                for (int i = 0; i < numAdditionalCurveIndexs; i++)
                {
                    m_AdditionalCurveIndexArray.Add(reader.ReadInt32());
                }
            }
            int numDeltas = reader.ReadInt32();
            m_ValueArrayDelta = new List<ValueDelta>(numDeltas);
            for (int i = 0; i < numDeltas; i++)
            {
                m_ValueArrayDelta.Add(new ValueDelta(reader));
            }
            if (version[0] > 5 || (version[0] == 5 && version[1] >= 3))//5.3 and up
            {
                m_ValueArrayReferencePose = reader.ReadSingleArray(reader.ReadInt32());
            }

            m_Mirror = reader.ReadBoolean();
            m_LoopTime = reader.ReadBoolean();
            m_LoopBlend = reader.ReadBoolean();
            m_LoopBlendOrientation = reader.ReadBoolean();
            m_LoopBlendPositionY = reader.ReadBoolean();
            m_LoopBlendPositionXZ = reader.ReadBoolean();
            if (version[0] > 5 || (version[0] == 5 && version[1] >= 5))//5.5 and up
            {
                m_StartAtOrigin = reader.ReadBoolean();
            }
            m_KeepOriginalOrientation = reader.ReadBoolean();
            m_KeepOriginalPositionY = reader.ReadBoolean();
            m_KeepOriginalPositionXZ = reader.ReadBoolean();
            m_HeightFromFeet = reader.ReadBoolean();
            reader.AlignStream(4);
        }
    }

    public class GenericBinding
    {
        public uint path { get; set; }
        public uint attribute { get; set; }
        public PPtr script { get; set; }
        public int typeID { get; set; }
        public byte customType { get; set; }
        public byte isPPtrCurve { get; set; }

        public GenericBinding(ObjectReader reader)
        {
            var version = reader.version;
            path = reader.ReadUInt32();
            attribute = reader.ReadUInt32();
            script = reader.ReadPPtr();
            if (version[0] > 5 || (version[0] == 5 && version[1] >= 6)) //5.6 and up
            {
                typeID = reader.ReadInt32();
            }
            else
            {
                typeID = reader.ReadUInt16();
            }
            customType = reader.ReadByte();
            isPPtrCurve = reader.ReadByte();
            reader.AlignStream(4);
        }
    }

    public class AnimationClipBindingConstant
    {
        public List<GenericBinding> genericBindings { get; set; }
        public List<PPtr> pptrCurveMapping { get; set; }

        public AnimationClipBindingConstant(ObjectReader reader)
        {
            int numBindings = reader.ReadInt32();
            genericBindings = new List<GenericBinding>(numBindings);
            for (int i = 0; i < numBindings; i++)
            {
                genericBindings.Add(new GenericBinding(reader));
            }

            int numMappings = reader.ReadInt32();
            pptrCurveMapping = new List<PPtr>(numMappings);
            for (int i = 0; i < numMappings; i++)
            {
                pptrCurveMapping.Add(reader.ReadPPtr());
            }
        }

        public GenericBinding FindBinding(int index)
        {
            int curves = 0;
            foreach (var b in genericBindings)
            {
                curves += b.attribute == 2 ? 4 : b.attribute <= 4 ? 3 : 1;
                if (curves > index)
                {
                    return b;
                }
            }

            return null;
        }
    }

    public enum AnimationType
    {
        kLegacy = 1,
        kGeneric = 2,
        kHumanoid = 3
    };

    public sealed class AnimationClip : NamedObject
    {
        public AnimationType m_AnimationType { get; set; }
        public bool m_Legacy { get; set; }
        public bool m_Compressed { get; set; }
        public bool m_UseHighQualityCurve { get; set; }
        public List<QuaternionCurve> m_RotationCurves { get; set; }
        public List<CompressedAnimationCurve> m_CompressedRotationCurves { get; set; }
        public List<Vector3Curve> m_EulerCurves { get; set; }
        public List<Vector3Curve> m_PositionCurves { get; set; }
        public List<Vector3Curve> m_ScaleCurves { get; set; }
        public List<FloatCurve> m_FloatCurves { get; set; }
        public List<PPtrCurve> m_PPtrCurves { get; set; }
        public float m_SampleRate { get; set; }
        public int m_WrapMode { get; set; }
        public AABB m_Bounds { get; set; }
        public uint m_MuscleClipSize { get; set; }
        public ClipMuscleConstant m_MuscleClip { get; set; }
        public AnimationClipBindingConstant m_ClipBindingConstant { get; set; }
        //public List<AnimationEvent> m_Events { get; set; }


        public AnimationClip(ObjectReader reader) : base(reader)
        {
            if (version[0] >= 5)//5.0 and up
            {
                m_Legacy = reader.ReadBoolean();
            }
            else if (version[0] >= 4)//4.0 and up
            {
                m_AnimationType = (AnimationType)reader.ReadInt32();
                if (m_AnimationType == AnimationType.kLegacy)
                    m_Legacy = true;
            }
            else
            {
                m_Legacy = true;
            }
            m_Compressed = reader.ReadBoolean();
            if (version[0] > 4 || (version[0] == 4 && version[1] >= 3))//4.3 and up
            {
                m_UseHighQualityCurve = reader.ReadBoolean();
            }
            reader.AlignStream(4);
            int numRCurves = reader.ReadInt32();
            m_RotationCurves = new List<QuaternionCurve>(numRCurves);
            for (int i = 0; i < numRCurves; i++)
            {
                m_RotationCurves.Add(new QuaternionCurve(reader));
            }

            int numCRCurves = reader.ReadInt32();
            m_CompressedRotationCurves = new List<CompressedAnimationCurve>(numCRCurves);
            for (int i = 0; i < numCRCurves; i++)
            {
                m_CompressedRotationCurves.Add(new CompressedAnimationCurve(reader));
            }

            if (version[0] > 5 || (version[0] == 5 && version[1] >= 3))//5.3 and up
            {
                int numEulerCurves = reader.ReadInt32();
                m_EulerCurves = new List<Vector3Curve>(numEulerCurves);
                for (int i = 0; i < numEulerCurves; i++)
                {
                    m_EulerCurves.Add(new Vector3Curve(reader));
                }
            }

            int numPCurves = reader.ReadInt32();
            m_PositionCurves = new List<Vector3Curve>(numPCurves);
            for (int i = 0; i < numPCurves; i++)
            {
                m_PositionCurves.Add(new Vector3Curve(reader));
            }

            int numSCurves = reader.ReadInt32();
            m_ScaleCurves = new List<Vector3Curve>(numSCurves);
            for (int i = 0; i < numSCurves; i++)
            {
                m_ScaleCurves.Add(new Vector3Curve(reader));
            }

            int numFCurves = reader.ReadInt32();
            m_FloatCurves = new List<FloatCurve>(numFCurves);
            for (int i = 0; i < numFCurves; i++)
            {
                m_FloatCurves.Add(new FloatCurve(reader));
            }

            if (version[0] > 4 || (version[0] == 4 && version[1] >= 3)) //4.3 and up
            {
                int numPtrCurves = reader.ReadInt32();
                m_PPtrCurves = new List<PPtrCurve>(numPtrCurves);
                for (int i = 0; i < numPtrCurves; i++)
                {
                    m_PPtrCurves.Add(new PPtrCurve(reader));
                }
            }

            m_SampleRate = reader.ReadSingle();
            m_WrapMode = reader.ReadInt32();
            if (version[0] > 3 || (version[0] == 3 && version[1] >= 4)) //3.4 and up
            {
                m_Bounds = new AABB(reader);
            }
            if (version[0] >= 4)//4.0 and up
            {
                m_MuscleClipSize = reader.ReadUInt32();
                m_MuscleClip = new ClipMuscleConstant(reader);
            }
            if (version[0] > 4 || (version[0] == 4 && version[1] >= 3)) //4.3 and up
            {
                m_ClipBindingConstant = new AnimationClipBindingConstant(reader);
            }
            /*int numEvents = reader.ReadInt32();
            m_Events = new List<AnimationEvent>(numEvents);
            for (int i = 0; i < numEvents; i++)
            {
                m_Events.Add(new AnimationEvent(stream, file.Version[0] - '0'));
            }*/
        }
    }
}
