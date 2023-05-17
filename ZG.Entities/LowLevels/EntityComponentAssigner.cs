using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    public struct EntityComponentAssigner
    {
        [BurstCompile]
        private struct AssignJob : IJobParallelFor
        {
            public BurstCompatibleTypeArray types;

            [ReadOnly]
            public EntityStorageInfoLookup entityStorageInfoLookup;

            [ReadOnly]
            public NativeArray<Entity> entityArray;

            [ReadOnly, NativeDisableContainerSafetyRestriction]
            public SharedHashMap<Entity, Entity>.Reader wrapper;
            
            [ReadOnly]
            public UnsafeParallelHashMap<int, int> typeHandleIndicess;

            [ReadOnly]
            public ReadOnly container;

            public unsafe void Execute(int index)
            {
                container.Apply(entityArray[index], entityStorageInfoLookup, wrapper, typeHandleIndicess, types);
            }
        }

        [BurstCompile]
        private struct ClearJob : IJob
        {
            public Container container;

            public unsafe void Execute()
            {
                container.Clear();
            }
        }

        [BurstCompile]
        private struct ResizeJob : IJob
        {
            public int elementSize;
            //public int elementCount;
            //public int entityCount;

            public int counterLength;

            [ReadOnly]
            public NativeArray<int> counts;

            public NativeArray<int> bufferSizeAndTypeCount;

            public Writer writer;

            public void Execute()
            {
                int elementCount, typeCount;
                switch (counterLength)
                {
                    case 0:
                        return;
                    case 1:
                        if (counts.Length < 1)
                            return;

                        elementCount = typeCount = counts[0];
                        break;
                    default:
                        if (counts.Length < 2)
                            return;

                        elementCount = counts[1];

                        typeCount = counts[0];
                        break;
                }

                int bufferSize = elementSize * elementCount;

                bufferSize = bufferSizeAndTypeCount[0] += bufferSize;
                typeCount = bufferSizeAndTypeCount[1] += typeCount;

                writer._Reset(bufferSize, typeCount);
            }
        }

        [BurstCompile]
        private struct DisposeTypeIndicesJob : IJob
        {
            public UnsafeParallelHashMap<int, int> value;

            public void Execute()
            {
                value.Dispose();
            }
        }

        internal struct Command
        {
            public enum Type
            {
                ComponentData,
                BufferOverride,
                BufferAppend,
                Enable, 
                Disable
            }

            public Type type;
            public UnsafeBlock block;
        }

        internal struct Key : System.IEquatable<Key>
        {
            public Entity entity;
            public TypeIndex typeIndex;

            public bool Equals(Key other)
            {
                return entity == other.entity && typeIndex == other.typeIndex;
            }

            public override int GetHashCode()
            {
                return entity.GetHashCode() ^ typeIndex;
            }
        }

        internal struct Value : System.IComparable<Value>
        {
            public int index;
            public Command command;

            public int CompareTo(Value other)
            {
                return index - other.index;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BurstCompatibleTypeArray
        {
            /*[NativeDisableContainerSafetyRestriction]*/ public DynamicComponentTypeHandle t0;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t1;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t2;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t3;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t4;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t5;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t6;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t7;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t8;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t9;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t10;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t11;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t12;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t13;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t14;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t15;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t16;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t17;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t18;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t19;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t20;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t21;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t22;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t23;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t24;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t25;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t26;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t27;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t28;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t29;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t30;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t31;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t32;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t33;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t34;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t35;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t36;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t37;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t38;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t39;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t40;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t41;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t42;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t43;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t44;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t45;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t46;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t47;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t48;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t49;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t50;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t51;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t52;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t53;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t54;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t55;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t56;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t57;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t58;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t59;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t60;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t61;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t62;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t63;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t64;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t65;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t66;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t67;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t68;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t69;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t70;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t71;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t72;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t73;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t74;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t75;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t76;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t77;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t78;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t79;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t80;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t81;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t82;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t83;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t84;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t85;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t86;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t87;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t88;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t89;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t90;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t91;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t92;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t93;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t94;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t95;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t96;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t97;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t98;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t99;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t100;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t101;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t102;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t103;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t104;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t105;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t106;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t107;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t108;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t109;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t110;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t111;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t112;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t113;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t114;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t115;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t116;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t117;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t118;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t119;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t120;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t121;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t122;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t123;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t124;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t125;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t126;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t127;
            /*[NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t128;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t129;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t130;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t131;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t132;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t133;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t134;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t135;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t136;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t137;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t138;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t139;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t140;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t141;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t142;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t143;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t144;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t145;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t146;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t147;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t148;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t149;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t150;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t151;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t152;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t153;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t154;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t155;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t156;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t157;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t158;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t159;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t160;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t161;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t162;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t163;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t164;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t165;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t166;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t167;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t168;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t169;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t170;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t171;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t172;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t173;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t174;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t175;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t176;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t177;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t178;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t179;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t180;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t181;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t182;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t183;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t184;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t185;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t186;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t187;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t188;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t189;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t190;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t191;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t192;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t193;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t194;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t195;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t196;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t197;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t198;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t199;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t200;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t201;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t202;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t203;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t204;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t205;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t206;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t207;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t208;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t209;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t210;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t211;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t212;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t213;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t214;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t215;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t216;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t217;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t218;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t219;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t220;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t221;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t222;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t223;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t224;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t225;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t226;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t227;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t228;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t229;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t230;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t231;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t232;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t233;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t234;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t235;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t236;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t237;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t238;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t239;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t240;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t241;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t242;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t243;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t244;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t245;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t246;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t247;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t248;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t249;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t250;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t251;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t252;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t253;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t254;
            [NativeDisableContainerSafetyRestriction] public DynamicComponentTypeHandle t255;*/

            public const int LENGTH = 128;

            public unsafe DynamicComponentTypeHandle this[int index]
            {
                get
                {
                    return ((DynamicComponentTypeHandle*)UnsafeUtility.AddressOf(ref this))[index];
                }

                set
                {
                    ((DynamicComponentTypeHandle*)UnsafeUtility.AddressOf(ref this))[index] = value;
                }
            }
        }

        internal struct Data
        {
            public UnsafeBufferEx buffer;
            public UnsafeParallelMultiHashMap<Entity, TypeIndex> entityTypes;
            public UnsafeParallelMultiHashMap<Key, Value> values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;

            internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<Data>();
#endif

            public AllocatorManager.AllocatorHandle allocator => buffer.allocator;

            public Data(AllocatorManager.AllocatorHandle allocator)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety =  CollectionHelper.CreateSafetyHandle(allocator);

                CollectionHelper.SetStaticSafetyId<EntityComponentAssigner>(ref m_Safety, ref StaticSafetyID.Data);
                AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif

                buffer = new UnsafeBufferEx(allocator, 1);
                entityTypes = new UnsafeParallelMultiHashMap<Entity, TypeIndex>(1, allocator);
                values = new UnsafeParallelMultiHashMap<Key, Value>(1, allocator);
            }

            public void Dispose()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);
                AtomicSafetyHandle.Release(m_Safety);
#endif

                buffer.Dispose();
                entityTypes.Dispose();
                values.Dispose();
            }

            public void Set(in Key key, in Command command)
            {
                Value value;
                value.index = values.CountValuesForKey(key);
                value.command = command;

                if (value.index == 0)
                    entityTypes.Add(key.entity, key.typeIndex);
                //Fail In Enable Or Disable
                /*else
                {
                    if (command.type == Command.Type.ComponentData)
                    {
                        values.Remove(key);

                        value.index = 0;
                    }
                }*/

                values.Add(key, value);
            }
        }

        [NativeContainer]
        internal struct Container
        {
            private UnsafeBufferEx __buffer;
            private UnsafeParallelMultiHashMap<Entity, TypeIndex> __entityTypes;
            private UnsafeParallelMultiHashMap<Key, Value> __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;

            internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<Container>();
#endif

            internal Data data
            {
                get
                {
                    Data data;
                    data.buffer = __buffer;
                    data.entityTypes = __entityTypes;
                    data.values = __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    data.m_Safety = m_Safety;
#endif

                    return data;
                }
            }

            internal Container(ref Data data)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = data.m_Safety;

                CollectionHelper.SetStaticSafetyId<Container>(ref m_Safety, ref StaticSafetyID.Data);
#endif

                __buffer = data.buffer;
                __entityTypes = data.entityTypes;
                __values = data.values;
            }

            /*public Allocator allocator => __buffer.allocator;

            public Container(Allocator allocator)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = AtomicSafetyHandle.Create();
#endif

                __buffer = new UnsafeBufferEx(allocator, 1);
                __entityTypes = new UnsafeParallelMultiHashMap<Entity, int>(1, allocator);
                __values = new UnsafeParallelMultiHashMap<Key, Value>(1, allocator);
            }*/

            /*public void Dispose()
            {
                __buffer.Dispose();
                __entityTypes.Dispose();
                __values.Dispose();
            }*/

            public void Clear()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
                __entityTypes.Clear();
                __values.Clear();
                __buffer.Reset();
            }

            public void Reset(int bufferSize, int typeCount)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                int length = __buffer.position + bufferSize;
                __buffer.capacity = math.max(__buffer.capacity, length);
                __buffer.length = math.max(__buffer.length, length);

                __entityTypes.Capacity = math.max(__entityTypes.Capacity, __entityTypes.Count() + typeCount);
                __values.Capacity = math.max(__values.Capacity, __values.Count() + typeCount);
            }

            public void Reset(int elementSize, int elementCount, int typeCount)
            {
                Reset(elementSize * elementCount, typeCount);
            }

            public bool TryGetComponentData<T>(in Entity entity, ref T value) where T : struct, IComponentData
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                int index = -1;

                Key key;
                key.entity = entity;
                key.typeIndex = TypeManager.GetTypeIndex<T>();

                if (__values.TryGetFirstValue(key, out var temp, out var iterator))
                {
                    do
                    {
                        if(temp.command.type == Command.Type.ComponentData && temp.index > index)
                        {
                            index = temp.index;

                            value = temp.command.block.As<T>();
                        }
                    } while (__values.TryGetNextValue(out temp, ref iterator));
                }

                return index != -1;
            }

            public bool TryGetBuffer<TValue, TList, TWrapper>(in Entity entity, ref TList list, ref TWrapper wrapper)
                where TValue : struct, IBufferElementData
                where TWrapper : IWriteOnlyListWrapper<TValue, TList>
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                Key key;
                key.entity = entity;
                key.typeIndex = TypeManager.GetTypeIndex<TValue>();

                int count = __values.CountValuesForKey(key);
                if (count < 1)
                    return false;

                var commands = new NativeArray<Command>(count, Allocator.Temp);
                {
                    if (__values.TryGetFirstValue(key, out var value, out var iterator))
                    {
                        do
                        {
                            commands[value.index] = value.command;
                        } while (__values.TryGetNextValue(out value, ref iterator));
                    }

                    Command command;
                    NativeArray<TValue> array;
                    int length, index, i, j;
                    for (i = 0; i < count; ++i)
                    {
                        command = commands[i];
                        array = command.block.isCreated ? command.block.AsArray<TValue>() : default;
                        length = array.IsCreated ? array.Length : 0;
                        switch (command.type)
                        {
                            case Command.Type.BufferOverride:
                                wrapper.SetCount(ref list, length);
                                for (j = 0; j < length; ++j)
                                    wrapper.Set(ref list, array[j], j);

                                break;
                            case Command.Type.BufferAppend:
                                index = wrapper.GetCount(list);
                                wrapper.SetCount(ref list, index + length);
                                for (j = 0; j < length; ++j)
                                    wrapper.Set(ref list, array[j], j + index);

                                break;
                            default:
                                break;
                        }
                    }
                }
                commands.Dispose();

                return true;
            }

            public bool TryGetBuffer<T>(in Entity entity, int index, ref T result, int indexOffset = 0)
                where T : struct, IBufferElementData
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                Key key;
                key.entity = entity;
                key.typeIndex = TypeManager.GetTypeIndex<T>();

                int count = __values.CountValuesForKey(key);
                if (count > 0)
                {
                    var commands = new NativeArray<Command>(count, Allocator.Temp);
                    {
                        if (__values.TryGetFirstValue(key, out var value, out var iterator))
                        {
                            do
                            {
                                commands[value.index] = value.command;
                            } while (__values.TryGetNextValue(out value, ref iterator));
                        }

                        Command command;
                        NativeArray<T> array;
                        int resultIndex, length, i;
                        for (i = 0; i < count; ++i)
                        {
                            command = commands[i];
                            array = command.block.isCreated ? command.block.AsArray<T>() : default;
                            length = array.IsCreated ? array.Length : 0;
                            switch (command.type)
                            {
                                case Command.Type.BufferOverride:
                                    indexOffset = length;
                                    break;
                                case Command.Type.BufferAppend:
                                    indexOffset += length;
                                    break;
                                default:
                                    break;
                            }

                            if (indexOffset > index)
                            {
                                resultIndex = length - indexOffset + index;
                                if(resultIndex >= 0 && resultIndex < length)
                                    result = array[resultIndex];
                            }
                        }
                    }
                    commands.Dispose();

                    return true;
                }

                return false;
            }

            public void SetComponentData<T>(in TypeIndex typeIndex, in Entity entity, in T value) where T : struct
            {
                __CheckTypeSize<T>(typeIndex);
                __CheckWrite();

                Command command;
                command.type = Command.Type.ComponentData;
                command.block = __buffer.writer.WriteBlock(UnsafeUtility.SizeOf<T>(), false);
                command.block.As<T>() = value;

                __Set(typeIndex, entity, command);
            }

            public void SetComponentData<T>(in Entity entity, in T value) where T : struct, IComponentData
            {
                /*__CheckWrite();

                Command command;
                command.type = Command.Type.ComponentData;
                command.block = __buffer.writer.WriteBlock(UnsafeUtility.SizeOf<T>(), false);
                command.block.As<T>() = value;

                __Set<T>(entity, command);*/

                SetComponentData(TypeManager.GetTypeIndex<T>(), entity, value);
            }

            public unsafe void SetBuffer<T>(bool isOverride, in Entity entity, void* values, int length)
                where T : struct, IBufferElementData
            {
                __CheckWrite();

                Command command;
                command.type = isOverride ? Command.Type.BufferOverride : Command.Type.BufferAppend;
                if (values == null || length < 1)
                    command.block = UnsafeBlock.Empty;
                else
                {
                    int size = UnsafeUtility.SizeOf<T>() * length;

                    command.block = __buffer.writer.WriteBlock(size, false);
                    command.block.writer.Write(values, size);
                }

                __Set<T>(entity, command);
            }

            public unsafe void SetBuffer<T>(bool isOverride, in Entity entity, in NativeArray<T> values) where T : struct, IBufferElementData => SetBuffer<T>(
                isOverride, entity, values.GetUnsafeReadOnlyPtr(), values.Length);

            public unsafe void SetBuffer<T>(bool isOverride, in Entity entity, in T[] values) where T : unmanaged, IBufferElementData
            {
                if (values == null)
                {
                    SetBuffer<T>(isOverride, entity, null, 0);

                    return;
                }

                fixed (void* ptr = values)
                {
                    SetBuffer<T>(isOverride, entity, ptr, values.Length);
                }
            }

            public unsafe void SetBuffer<TValue, TCollection>(bool isOverride, in Entity entity, in TCollection values)
                where TValue : struct, IBufferElementData
                where TCollection : IReadOnlyCollection<TValue>
            {
                __CheckWrite();

                Command command;
                command.type = isOverride ? Command.Type.BufferOverride : Command.Type.BufferAppend;

                int count = values == null ? 0 : values.Count;
                if (count > 0)
                {
                    command.block = __buffer.writer.WriteBlock(UnsafeUtility.SizeOf<TValue>() * count, false);

                    int index = 0;
                    var array = command.block.AsArray<TValue>();
                    foreach (var value in values)
                    {
                        array[index++] = value;

                        UnityEngine.Assertions.Assert.IsTrue(index <= count);
                    }
                }
                else
                    command.block = UnsafeBlock.Empty;

                __Set<TValue>(entity, command);
            }

            public void SetComponentEnabled<T>(in Entity entity, bool value) where T : struct, IEnableableComponent
            {
                __CheckWrite();

                Command command;
                command.type = value ? Command.Type.Enable : Command.Type.Disable;
                command.block = UnsafeBlock.Empty;

                __Set<T>(entity, command);
            }

            public bool RemoveComponent<T>(in Entity entity)
            {
                __CheckWrite();

                Key key;
                key.typeIndex = TypeManager.GetTypeIndex<T>();
                key.entity = entity;
                if (__values.Remove(key) > 0)
                {
                    if (__entityTypes.TryGetFirstValue(entity, out var typeIndex, out var iterator))
                    {
                        do
                        {
                            if (typeIndex == key.typeIndex)
                            {
                                __entityTypes.Remove(iterator);

                                return true;
                            }
                        } while (__entityTypes.TryGetNextValue(out typeIndex, ref iterator));
                    }

                    __CheckEntityType();
                }

                return false;
            }

            public unsafe bool Apply(
                ref SystemState systemState,
                in SharedHashMap<Entity, Entity>.Reader entities,
                SharedHashMap<Entity, Entity> wrapper,
                int innerloopBatchCount = 1)
            {
                if (__entityTypes.IsEmpty)
                    return false;

                AssignJob assign;
                assign.types = default;
                assign.entityStorageInfoLookup = systemState.GetEntityStorageInfoLookup();
                assign.wrapper = wrapper.isCreated ? wrapper.reader : default;
                assign.container = new ReadOnly(data);

                var jobHandle = wrapper.isCreated ? JobHandle.CombineDependencies(systemState.Dependency, wrapper.lookupJobManager.readOnlyJobHandle) : systemState.Dependency;
                var keys = __entityTypes.GetKeyArray(systemState.WorldUpdateAllocator);
                {
                    NativeParallelMultiHashMapIterator<Entity> iterator;

                    ComponentType componentType;
                    componentType.AccessModeType = ComponentType.AccessMode.ReadWrite;

                    int numTypes = 0, numEntities = keys.ConvertToUniqueArray(), entityIndex = 0, i;
                    var typeHandleIndicess = new UnsafeParallelHashMap<int, int>(BurstCompatibleTypeArray.LENGTH, Allocator.TempJob);

                    assign.typeHandleIndicess = typeHandleIndicess;

                    Entity key, entity;
                    DisposeTypeIndicesJob disposeTypeIndices;
                    for (i = 0; i < numEntities; ++i)
                    {
                        key = keys[i];

                        if (entities.isCreated)
                        {
                            if (entities.TryGetValue(key, out entity))
                                keys[i] = entity;
                            else
                            {
                                keys[i--] = keys[--numEntities];

                                continue;
                            }
                        }

                        if (__entityTypes.TryGetFirstValue(key, out componentType.TypeIndex, out iterator))
                        {
                            do
                            {
                                if (typeHandleIndicess.ContainsKey(componentType.TypeIndex))
                                    continue;

                                if (numTypes >= BurstCompatibleTypeArray.LENGTH)
                                {
                                    if (i <= entityIndex)
                                        UnityEngine.Debug.LogError($"{key} Component More Than {BurstCompatibleTypeArray.LENGTH}");

                                    __CheckComponent(i, entityIndex);

                                    assign.entityArray = keys.GetSubArray(entityIndex, i - entityIndex);

                                    //assign.RunBurstCompatible(group, keys.GetSubArray(entityIndex, i - entityIndex));
                                    jobHandle = assign.ScheduleByRef(assign.entityArray.Length, innerloopBatchCount, jobHandle);

                                    entityIndex = i--;

                                    numTypes = 0;

                                    disposeTypeIndices.value = typeHandleIndicess;
                                    jobHandle = disposeTypeIndices.Schedule(jobHandle);

                                    typeHandleIndicess = new UnsafeParallelHashMap<int, int>(BurstCompatibleTypeArray.LENGTH, Allocator.TempJob);
                                    assign.typeHandleIndicess = typeHandleIndicess;

                                    break;
                                }

                                typeHandleIndicess[componentType.TypeIndex] = numTypes;

                                assign.types[numTypes++] = systemState.GetDynamicComponentTypeHandle(componentType);
                            } while (__entityTypes.TryGetNextValue(out componentType.TypeIndex, ref iterator));
                        }
                    }

                    if (numTypes > 0)
                    {
                        assign.entityArray = keys.GetSubArray(entityIndex, numEntities - entityIndex);
                        jobHandle = assign.ScheduleByRef(assign.entityArray.Length, innerloopBatchCount, jobHandle);
                    }

                    disposeTypeIndices.value = typeHandleIndicess;
                    jobHandle = disposeTypeIndices.Schedule(jobHandle);
                }
                //keys.Dispose();

                if (wrapper.isCreated)
                    wrapper.lookupJobManager.AddReadOnlyDependency(jobHandle);

                systemState.Dependency = jobHandle;

                return true;
            }

            public unsafe bool Playback(
                ref SystemState systemState, 
                in SharedHashMap<Entity, Entity>.Reader entities, 
                SharedHashMap<Entity, Entity> wrapper,
                int innerloopBatchCount = 1)
            {
                if (Apply(ref systemState, entities, wrapper, innerloopBatchCount))
                {
                    ClearJob clear;
                    clear.container = this;
                    systemState.Dependency = clear.Schedule(systemState.Dependency);

                    return true;
                }

                return false;
            }

            /*private void __Set(in Key key, in Command command)
            {
                Value value;
                value.index = __values.CountValuesForKey(key);
                value.command = command;

                if (value.index > 0)
                {
                    if (command.type == Command.Type.ComponentData)
                    {
                        __values.Remove(key);

                        value.index = 0;
                    }
                }
                else
                    __entityTypes.Add(key.entity, key.typeIndex);

                __values.Add(key, value);
            }*/

            private void __Set<T>(in Entity entity, in Command command) where T : struct
            {
                Key key;
                key.entity = entity;
                key.typeIndex = TypeManager.GetTypeIndex<T>();

                data.Set(key, command);
                //__Set(key, command);

                /*Value value;
                value.index = __values.CountValuesForKey(key);
                value.command = command;

                if (value.index > 0)
                {
                    if (command.type == Command.Type.ComponentData)
                    {
                        __values.Remove(key);

                        value.index = 0;
                    }
                }
                else
                    __entityTypes.Add(key.entity, key.typeIndex);

                __values.Add(key, value);*/
            }

            private void __Set(int typeIndex, in Entity entity, in Command command)
            {
                Key key;
                key.entity = entity;
                key.typeIndex = typeIndex;

                data.Set(key, command);
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            static void __CheckTypeSize<T>(int typeIndex) where T : struct
            {
                if (UnsafeUtility.SizeOf<T>() != TypeManager.GetTypeInfo(typeIndex).ElementSize)
                    throw new System.InvalidCastException();
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void __CheckWrite()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            }
        }

        [NativeContainer]
        public struct ReadOnly
        {
            private UnsafeParallelMultiHashMap<Entity, TypeIndex> __entityTypes;

            private UnsafeParallelMultiHashMap<Key, Value> __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            internal ReadOnly(in Data data)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                //AtomicSafetyHandle.CheckReadAndThrow(data.m_Safety);

                m_Safety = data.m_Safety;

                AtomicSafetyHandle.UseSecondaryVersion(ref m_Safety);

                //UnityEngine.Debug.LogError($"dddd {AtomicSafetyHandle.GetAllowReadOrWriteAccess(m_Safety)}");
#endif

                __entityTypes = data.entityTypes;
                __values = data.values;
            }

            public bool AppendTo(Writer assigner, in NativeArray<Entity> entityArray = default)
            {
                var data = assigner.data;
                return AppendTo(ref data, entityArray);
            }

            internal bool AppendTo(ref Data data, in NativeArray<Entity> entityArray = default)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                if (__values.IsEmpty)
                    return false;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(data.m_Safety);
#endif

                bool result = false;
                using (var keys = __values.GetKeyArray(Allocator.Temp))
                {
                    var values = new NativeList<Value>(Allocator.Temp);

                    UnsafeBufferEx.Writer writer = data.buffer.writer;
                    NativeParallelMultiHashMapIterator<Key> iterator;
                    Value value;
                    Key key;
                    Command destination;
                    int i, j, numValues, numKeys = keys.ConvertToUniqueArray();
                    for (i = 0; i < numKeys; ++i)
                    {
                        key = keys[i];
                        if (entityArray.IsCreated && !entityArray.Contains(key.entity))
                            continue;

                        if (__values.TryGetFirstValue(key, out value, out iterator))
                        {
                            result = true;

                            values.Clear();

                            do
                            {
                                values.Add(value);
                            } while (__values.TryGetNextValue(out value, ref iterator));

                            values.Sort();

                            numValues = values.Length;
                            for (j = 0; j < numValues; ++j)
                            {
                                ref var source = ref values.ElementAt(j).command;

                                destination.type = source.type;

                                destination.block = source.block.isCreated ? writer.WriteBlock(source.block) : UnsafeBlock.Empty;

                                data.Set(key, destination);
                            }
                        }
                    }

                    values.Dispose();
                }

                return result;
            }

            internal unsafe void Apply(
                in Entity entity,
                in EntityStorageInfoLookup entityStorageInfoLookup,
                in SharedHashMap<Entity, Entity>.Reader wrapper,
                in UnsafeParallelHashMap<int, int> typeHandleIndicess,
                in BurstCompatibleTypeArray types)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                var values = new NativeList<Value>(Allocator.Temp);
                DynamicComponentTypeHandle dynamicComponentTypeHandle;
                NativeParallelMultiHashMapIterator<Entity> entityIterator;
                NativeParallelMultiHashMapIterator<Key> keyIterator;
                Key key;
                Value value;
                Command command;
                EntityStorageInfo entityStorageInfo;
                Unity.Entities.LowLevel.Unsafe.UnsafeUntypedBufferAccessor bufferAccessor = default;
                void* source, destination = null;
                int blockSize, numValues, elementSize, j;

                key.entity = wrapper.isCreated ? wrapper[entity] : entity;

                if (__entityTypes.TryGetFirstValue(key.entity, out key.typeIndex, out entityIterator))
                {
                    do
                    {
                        entityStorageInfo = entityStorageInfoLookup[entity];
                        dynamicComponentTypeHandle = types[typeHandleIndicess[key.typeIndex]];
                        dynamicComponentTypeHandle.m_TypeLookupCache = (short)key.typeIndex;

                        if (!entityStorageInfo.Chunk.Has(ref dynamicComponentTypeHandle))
                            continue;

                        elementSize = TypeManager.GetTypeInfo(key.typeIndex).ElementSize;

                        if (TypeManager.IsBuffer(key.typeIndex))
                            bufferAccessor = entityStorageInfo.Chunk.GetUntypedBufferAccessor(ref dynamicComponentTypeHandle);
                        else
                            destination = (byte*)entityStorageInfo.Chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref dynamicComponentTypeHandle, elementSize).GetUnsafePtr() + entityStorageInfo.IndexInChunk * elementSize;

                        values.Clear();

                        if (__values.TryGetFirstValue(key, out value, out keyIterator))
                        {
                            do
                            {
                                values.Add(value);
                            } while (__values.TryGetNextValue(out value, ref keyIterator));
                        }

                        values.Sort();

                        numValues = values.Length;
                        for (j = 0; j < numValues; ++j)
                        {
                            command = values.ElementAt(j).command;
                            if (command.block.isCreated)
                                source = command.block.GetRangePtr(out blockSize);
                            else
                            {
                                source = null;

                                blockSize = 0;
                            }

                            switch (command.type)
                            {
                                case Command.Type.ComponentData:
                                    UnityEngine.Assertions.Assert.AreEqual(blockSize, elementSize);

                                    //destination = (byte*)batchInChunk.GetDynamicComponentDataArrayReinterpret<byte>(dynamicComponentTypeHandle, blockSize).GetUnsafePtr() + i * blockSize;
                                    break;
                                case Command.Type.BufferOverride:
                                case Command.Type.BufferAppend:
                                    //var bufferAccessor = batchInChunk.GetUntypedBufferAccessor(ref dynamicComponentTypeHandle);
                                    int elementCount = blockSize / elementSize;
                                    if (command.type == Command.Type.BufferOverride)
                                    {
                                        bufferAccessor.ResizeUninitialized(entityStorageInfo.IndexInChunk, elementCount);
                                        destination = bufferAccessor.GetUnsafePtr(entityStorageInfo.IndexInChunk);
                                    }
                                    else
                                    {
                                        int originCount = bufferAccessor.GetBufferLength(entityStorageInfo.IndexInChunk);
                                        bufferAccessor.ResizeUninitialized(entityStorageInfo.IndexInChunk, originCount + elementCount);
                                        destination = (byte*)bufferAccessor.GetUnsafePtr(entityStorageInfo.IndexInChunk) + originCount * elementSize;
                                    }
                                    break;
                                case Command.Type.Enable:
                                    entityStorageInfo.Chunk.SetComponentEnabled(ref dynamicComponentTypeHandle, entityStorageInfo.IndexInChunk, true);
                                    break;
                                case Command.Type.Disable:
                                    entityStorageInfo.Chunk.SetComponentEnabled(ref dynamicComponentTypeHandle, entityStorageInfo.IndexInChunk, false);
                                    break;
                            }

                            if (source != null)
                                UnsafeUtility.MemCpy(destination, source, blockSize);
                        }
                    } while (__entityTypes.TryGetNextValue(out key.typeIndex, ref entityIterator));
                }

                values.Dispose();
            }
        }

        public struct Writer
        {
            private Container __container;

            internal Data data => __container.data;

            internal Writer(ref Data data)
            {
                __container = new Container(ref data);

/*#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.UseSecondaryVersion(ref __container.m_Safety);
#endif*/
            }

            public void SetComponentData<T>(in TypeIndex typeIndex, in Entity entity, in T value) where T : struct => __container.SetComponentData(typeIndex, entity, value);

            public void SetComponentData<T>(in Entity entity, in T value) where T : struct, IComponentData => __container.SetComponentData(entity, value);

            public unsafe void SetBuffer<T>(bool isOverride, in Entity entity, void* values, int length)
                where T : struct, IBufferElementData => __container.SetBuffer<T>(isOverride, entity, values, length);

            public unsafe void SetBuffer<T>(bool isOverride, in Entity entity, in NativeArray<T> values) where T : struct, IBufferElementData =>
                __container.SetBuffer(isOverride, entity, values);

            public void Clear()
            {
                __container.Clear();
            }

            internal void _Reset(int bufferSize, int typeCount) => __container.Reset(bufferSize, typeCount);
        }

        [NativeContainer, NativeContainerIsAtomicWriteOnly]
        public struct ParallelWriter
        {
            private UnsafeBufferEx.ParallelWriter __buffer;
            private UnsafeParallelMultiHashMap<Entity, TypeIndex>.ParallelWriter __entityTypes;
            private UnsafeParallelMultiHashMap<Key, Value>.ParallelWriter __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            internal ParallelWriter(ref Data data)
            {
                __buffer = data.buffer.parallelWriter;
                __entityTypes = data.entityTypes.AsParallelWriter();
                __values = data.values.AsParallelWriter();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = data.m_Safety;

                AtomicSafetyHandle.UseSecondaryVersion(ref m_Safety);
#endif
            }

            public void SetComponentData<T>(int typeIndex, in Entity entity, in T value) where T : struct, IComponentData
            {
                __CheckTypeSize<T>(typeIndex);
                __CheckWrite();

                Command command;
                command.type = Command.Type.ComponentData;
                command.block = __buffer.WriteBlock(UnsafeUtility.SizeOf<T>(), false);
                command.block.As<T>() = value;

                __Set(typeIndex, entity, command);
            }

            public void SetComponentData<T>(in Entity entity, in T value) where T : struct, IComponentData => SetComponentData(TypeManager.GetTypeIndex<T>(), entity, value);

            public unsafe void AppendBuffer<T>(int typeIndex, in Entity entity, void* values, int length) where T : struct, IBufferElementData
            {
                __CheckTypeSize<T>(typeIndex);
                __CheckWrite();

                Command command;
                command.type = Command.Type.BufferAppend;

                int size = UnsafeUtility.SizeOf<T>() * length;

                command.block = __buffer.WriteBlock(size, false);
                command.block.writer.Write(values, size);

                __Set(typeIndex, entity, command);
            }

            public unsafe void AppendBuffer<T>(int typeIndex, in Entity entity, ref T value) where T : struct, IBufferElementData => AppendBuffer<T>(typeIndex, entity, UnsafeUtility.AddressOf(ref value), 1);

            public unsafe void AppendBuffer<T>(int typeIndex, in Entity entity, NativeArray<T> values) where T : struct, IBufferElementData => AppendBuffer<T>(typeIndex, entity, NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(values), values.Length);

            public void AppendBuffer<T>(in Entity entity, ref T value) where T : struct, IBufferElementData => AppendBuffer<T>(TypeManager.GetTypeIndex<T>(), entity, ref value);

            public void AppendBuffer<T>(in Entity entity, NativeArray<T> values) where T : struct, IBufferElementData => AppendBuffer<T>(TypeManager.GetTypeIndex<T>(), entity, values);

            private void __Set(int typeIndex, in Entity entity, in Command command)
            {
                __entityTypes.Add(entity, typeIndex);

                Key key;
                key.entity = entity;
                key.typeIndex = typeIndex;

                Value value;
                value.index = 0;
                value.command = command;

                __values.Add(key, value);
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            static void __CheckTypeSize<T>(int typeIndex) where T : struct
            {
                if (UnsafeUtility.SizeOf<T>() != TypeManager.GetTypeInfo(typeIndex).ElementSize)
                    throw new System.InvalidCastException();
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void __CheckWrite()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            }
        }

        /*[NativeContainer, NativeContainerIsAtomicWriteOnly]
        public struct ComponentDataParallelWriter<T> where T : struct, IComponentData
        {
            private UnsafeBufferEx.ParallelWriter __buffer;
            private UnsafeParallelMultiHashMap<Entity, int>.ParallelWriter __entityTypes;
            private UnsafeParallelMultiHashMap<Key, Value>.ParallelWriter __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            internal ComponentDataParallelWriter(ref Data data)
            {
                __buffer = data.buffer.parallelWriter;
                __entityTypes = data.entityTypes.AsParallelWriter();
                __values = data.values.AsParallelWriter();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = data.m_Safety;

                AtomicSafetyHandle.UseSecondaryVersion(ref m_Safety);
#endif
            }

            public void SetComponentData(int typeIndex, in Entity entity, in T value)
            {
                __CheckTypeSize(typeIndex);
                __CheckWrite();

                Command command;
                command.type = Command.Type.ComponentData;
                command.block = __buffer.WriteBlock(UnsafeUtility.SizeOf<T>(), false);
                command.block.As<T>() = value;

                __Set(typeIndex, entity, command);
            }

            public void SetComponentData(in Entity entity, in T value) => SetComponentData(TypeManager.GetTypeIndex<T>(), entity, value);

            private void __Set(int typeIndex, in Entity entity, in Command command)
            {
                __entityTypes.Add(entity, typeIndex);

                Key key;
                key.entity = entity;
                key.typeIndex = typeIndex;

                Value value;
                value.index = 0;
                value.command = command;

                __values.Add(key, value);
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            static void __CheckTypeSize(int typeIndex)
            {
                if (UnsafeUtility.SizeOf<T>() != TypeManager.GetTypeInfo(typeIndex).ElementSize)
                    throw new System.InvalidCastException();
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void __CheckWrite()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            }
        }

        [NativeContainer, NativeContainerIsAtomicWriteOnly]
        public struct BufferParallelWriter<T> where T : struct, IBufferElementData
        {
            private UnsafeBufferEx.ParallelWriter __buffer;
            private UnsafeParallelMultiHashMap<Entity, int>.ParallelWriter __entityTypes;
            private UnsafeParallelMultiHashMap<Key, Value>.ParallelWriter __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            internal BufferParallelWriter(ref Data data)
            {
                __buffer = data.buffer.parallelWriter;
                __entityTypes = data.entityTypes.AsParallelWriter();
                __values = data.values.AsParallelWriter();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = data.m_Safety;

                AtomicSafetyHandle.UseSecondaryVersion(ref m_Safety);
#endif
            }

            public unsafe void AppendBuffer(int typeIndex, in Entity entity, void* values, int length)
            {
                __CheckTypeSize(typeIndex);
                __CheckWrite();

                Command command;
                command.type = Command.Type.BufferAppend;

                int size = UnsafeUtility.SizeOf<T>() * length;

                command.block = __buffer.WriteBlock(size, false);
                command.block.writer.Write(values, size);

                __Set(typeIndex, entity, command);
            }

            public unsafe void AppendBuffer(int typeIndex, in Entity entity, ref T value) => AppendBuffer(typeIndex, entity, UnsafeUtility.AddressOf(ref value), 1);

            public unsafe void AppendBuffer(int typeIndex, in Entity entity, NativeArray<T> values) => AppendBuffer(typeIndex, entity, NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(values), values.Length);

            public void AppendBuffer(in Entity entity, ref T value) => AppendBuffer(TypeManager.GetTypeIndex<T>(), entity, ref value);

            public void AppendBuffer(in Entity entity, NativeArray<T> values) => AppendBuffer(TypeManager.GetTypeIndex<T>(), entity, values);

            private void __Set(int typeIndex, in Entity entity, in Command command)
            {
                __entityTypes.Add(entity, typeIndex);

                Key key;
                key.entity = entity;
                key.typeIndex = typeIndex;

                Value value;
                value.index = 0;
                value.command = command;

                __values.Add(key, value);
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            static void __CheckTypeSize(int typeIndex)
            {
                if (UnsafeUtility.SizeOf<T>() != TypeManager.GetTypeInfo(typeIndex).ElementSize)
                    throw new System.InvalidCastException();
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void __CheckWrite()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            }
        }*/

        private unsafe JobHandle* __jobHandle;

        private NativeArray<int> __bufferSizeAndTypeCount;

        private Data __data;

        public unsafe bool isCreated => __jobHandle != null;

        public int bufferSize
        {
            get => __bufferSizeAndTypeCount[0];

            private set
            {
                __bufferSizeAndTypeCount[0] = value;
            }
        }

        public int typeCount
        {
            get => __bufferSizeAndTypeCount[1];

            private set
            {
                __bufferSizeAndTypeCount[1] = value;
            }
        }

        public unsafe JobHandle jobHandle
        {
            get => *__jobHandle;

            set => *__jobHandle = value;
        }

        public Writer writer => new Writer(ref __data);

        internal Container container => new Container(ref __data);

        public unsafe EntityComponentAssigner(in AllocatorManager.AllocatorHandle allocator)
        {
            __jobHandle = AllocatorManager.Allocate<JobHandle>(allocator);
            *__jobHandle = default;

            __bufferSizeAndTypeCount = new NativeArray<int>(2, (Allocator)allocator.Value, NativeArrayOptions.ClearMemory);

            __data = new Data(allocator);
        }

        public unsafe void Dispose()
        {
            jobHandle.Complete();

            AllocatorManager.Free(__data.allocator, __jobHandle);
            __jobHandle = null;

            __bufferSizeAndTypeCount.Dispose();

            __data.Dispose();
        }

        public void Clear()
        {
            CompleteDependency();

            bufferSize = 0;
            typeCount = 0;

            container.Clear();
        }

        public ReadOnly AsReadOnly()
        {
            return new ReadOnly(__data);
        }

        public void CompleteDependency()
        {
            jobHandle.Complete();
            jobHandle = default;
        }

        public ParallelWriter AsParallelWriter(int bufferSize, int typeCount)
        {
            CompleteDependency();

            bufferSize = this.bufferSize += bufferSize;
            typeCount = this.typeCount += typeCount;

            writer._Reset(bufferSize, typeCount);

            return new ParallelWriter(ref __data);
        }

        public ParallelWriter AsComponentDataParallelWriter<T>(int entityCount) where T : struct, IComponentData
        {
            return AsParallelWriter(UnsafeUtility.SizeOf<T>() * entityCount, entityCount);
        }

        public ParallelWriter AsBufferParallelWriter<T>(int elementCount, int typeCount) where T : struct, IBufferElementData
        {
            return AsParallelWriter(UnsafeUtility.SizeOf<T>() * elementCount, typeCount);
        }

        public ParallelWriter AsComponentDataParallelWriter<T>(in NativeArray<int> entityCount, ref JobHandle jobHandle) where T  : struct, IComponentData
        {
            ResizeJob resize;
            resize.elementSize = UnsafeUtility.SizeOf<T>();
            resize.counterLength = 1;
            resize.counts = entityCount;
            resize.bufferSizeAndTypeCount = __bufferSizeAndTypeCount;
            resize.writer = writer;
            jobHandle = resize.Schedule(JobHandle.CombineDependencies(jobHandle, this.jobHandle));

            this.jobHandle = jobHandle;

            return new ParallelWriter(ref __data);
        }

        public ParallelWriter AsBufferParallelWriter<T>(in NativeArray<int> typeAndBufferCounts, ref JobHandle jobHandle) where T : struct, IBufferElementData
        {
            ResizeJob resize;
            resize.elementSize = UnsafeUtility.SizeOf<T>();
            resize.counterLength = 2;
            resize.counts = typeAndBufferCounts;
            resize.bufferSizeAndTypeCount = __bufferSizeAndTypeCount;
            resize.writer = writer;
            jobHandle = resize.Schedule(JobHandle.CombineDependencies(jobHandle, this.jobHandle));

            this.jobHandle = jobHandle;

            return new ParallelWriter(ref __data);
        }

        public bool TryGetComponentData<T>(in Entity entity, ref T value) where T : struct, IComponentData
        {
            CompleteDependency();

            return container.TryGetComponentData(entity, ref value);
        }

        public bool TryGetBuffer<TValue, TList, TWrapper>(in Entity entity, ref TList list, ref TWrapper wrapper) 
            where TValue : struct, IBufferElementData
            where TWrapper : IWriteOnlyListWrapper<TValue, TList>
        {
            CompleteDependency();

            return container.TryGetBuffer<TValue, TList, TWrapper>(entity, ref list, ref wrapper);
        }

        public bool TryGetBuffer<T>(in Entity entity, int index, ref T value, int indexOffset = 0)
            where T : struct, IBufferElementData
        {
            CompleteDependency();

            return container.TryGetBuffer(entity, index, ref value, indexOffset);
        }

        public void SetComponentData<T>(in Entity entity, in T value) where T : struct, IComponentData
        {
            CompleteDependency();

            container.SetComponentData(entity, value);
        }

        public unsafe void SetBuffer<T>(bool isOverride, in Entity entity, void* values, int length)
            where T : struct, IBufferElementData
        {
            CompleteDependency();

            container.SetBuffer<T>(isOverride, entity, values, length);
        }

        public unsafe void SetBuffer<T>(bool isOverride, in Entity entity, T value)
            where T : struct, IBufferElementData
        {
            SetBuffer<T>(isOverride, entity, UnsafeUtility.AddressOf(ref value), 1);
        }

        public void SetBuffer<T>(bool isOverride, in Entity entity, in NativeArray<T> values)
            where T : struct, IBufferElementData
        {
            CompleteDependency();

            container.SetBuffer(isOverride, entity, values);
        }

        public void SetBuffer<T>(bool isOverride, in Entity entity, in T[] values) where T : unmanaged, IBufferElementData
        {
            CompleteDependency();

            container.SetBuffer(isOverride, entity, values);
        }

        public void SetBuffer<TValue, TCollection>(bool isOverride, in Entity entity, in TCollection values)
            where TValue : struct, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            CompleteDependency();

            container.SetBuffer<TValue, TCollection>(isOverride, entity, values);
        }

        public void SetComponentEnabled<T>(in Entity entity, bool value) where T : struct, IEnableableComponent
        {
            CompleteDependency();

            container.SetComponentEnabled<T>(entity, value);
        }

        public bool RemoveComponent<T>(in Entity entity)
        {
            CompleteDependency();

            return container.RemoveComponent<T>(entity);
        }

        public bool Apply(
            ref SystemState systemState,
            in SharedHashMap<Entity, Entity>.Reader entities = default,
            SharedHashMap<Entity, Entity> wrapper = default,
            int innerloopBatchCount = 1)
        {
            jobHandle.Complete();

            if (container.Apply(ref systemState, entities, wrapper, innerloopBatchCount))
            {
                jobHandle = systemState.Dependency;

                return true;
            }

            jobHandle = default;

            bufferSize = 0;
            typeCount = 0;

            return false;
        }

        public void Playback(
            ref SystemState systemState, 
            in SharedHashMap<Entity, Entity>.Reader entities = default,
            SharedHashMap<Entity, Entity> wrapper = default,
            int innerloopBatchCount = 1)
        {
            jobHandle.Complete();
            if (container.Playback(ref systemState, entities, wrapper, innerloopBatchCount))
                jobHandle = systemState.Dependency;
            else
                jobHandle = default;

            bufferSize = 0;
            typeCount = 0;
        }

        public unsafe long GetHashCode64()
        {
            return (long)__jobHandle;
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void __InitJobs()
        {
            BurstUtility.InitializeJob<ClearJob>();
            BurstUtility.InitializeJob<ResizeJob>();
            BurstUtility.InitializeJob<DisposeTypeIndicesJob>();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void __CheckComponent(int i, int entityIndex)
        {
            if (i <= entityIndex)
                throw new System.InvalidCastException();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void __CheckEntityType()
        {
            throw new System.Exception();
        }
    }

    public struct EntitySharedComponentAssigner
    {
        private interface IHandler
        {
            bool Delete(in Entity entity);

            void Playback(EntityManager entityManager);
        }

        private class Handler<T> : IHandler where T : struct, ISharedComponentData
        {
            private Dictionary<Entity, T> __values;

            public Handler()
            {
                __values = new Dictionary<Entity, T>();
            }

            public bool TryGetValue(Entity entity, out T value)
            {
                return __values.TryGetValue(entity, out value);
            }

            public void Set(in Entity entity, in T value)
            {
                __values[entity] = value;
            }

            public bool Delete(in Entity entity)
            {
                return __values.Remove(entity);
            }

            public void Playback(EntityManager entityManager)
            {
                foreach(var pair in __values)
                    entityManager.SetSharedComponentManaged(pair.Key, pair.Value);

                __values.Clear();
            }
        }

        private UnsafeParallelHashMap<int, GCHandle> __handles;

        public EntitySharedComponentAssigner(Allocator allocator)
        {
            __handles = new UnsafeParallelHashMap<int, GCHandle>(1, allocator);
        }

        public void Dispose()
        {
            using (var handles = __handles.GetValueArray(Allocator.Temp))
                __Dispose(handles);

            __handles.Dispose();
        }

        public bool TryGetSharedComponentData<T>(Entity entity, ref T value) where T : struct, ISharedComponentData
        {
            int typeIndex = TypeManager.GetTypeIndex<T>();
            if (__handles.TryGetValue(typeIndex, out var handle) &&
                ((Handler<T>)handle.Target).TryGetValue(entity, out var result))
            {
                value = result;

                return true;
            }

            return false;
        }

        public void SetSharedComponentData<T>(Entity entity, T value) where T : struct, ISharedComponentData
        {
            int typeIndex = TypeManager.GetTypeIndex<T>();
            Handler<T> handler;
            if (__handles.TryGetValue(typeIndex, out var handle))
                handler = (Handler<T>)handle.Target;
            else
            {
                handler = new Handler<T>();
                handle = GCHandle.Alloc(handler);
                __handles[typeIndex] = handle;
            }

            handler.Set(entity, value);
        }

        public bool RemoveComponent<T>(in Entity entity)
        {
            return __handles.TryGetValue(TypeManager.GetTypeIndex<T>(), out var handle) && ((IHandler)handle.Target).Delete(entity);
        }

        public void Playback(EntityManager entityManager)
        {
            if (__handles.IsEmpty)
                return;

            using (var handles = __handles.GetValueArray(Allocator.Temp))
                __Playback(handles, entityManager);
        }

        private static void __Playback(in NativeArray<GCHandle> handles, EntityManager entityManager)
        {
            IHandler handler;
            foreach (var handle in handles)
            {
                handler = (IHandler)handle.Target;

                handler.Playback(entityManager);
            }
        }

        private static void __Dispose(in NativeArray<GCHandle> handles)
        {
            foreach (var handle in handles)
                handle.Free();
        }
    }
}