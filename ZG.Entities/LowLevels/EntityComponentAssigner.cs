using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System;

namespace ZG
{
    public struct EntityComponentAssigner : IDisposable
    {
        public enum BufferOption
        {
            Override, 
            Append,
            AppendUnique,
            Remove,
            RemoveSwapBack
        }

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
            public UnsafeHashMap<int, int> typeHandleIndicess;

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

            [NativeDisableUnsafePtrRestriction]
            public unsafe int* bufferSize;
            [NativeDisableUnsafePtrRestriction]
            public unsafe int* typeCount;

            public Writer writer;

            public unsafe void Execute()
            {
                int elementCount, typeCount, bufferSize;
                switch (counterLength)
                {
                    case 1:
                        if (counts.Length < 1)
                            return;

                        typeCount = counts[0];

                        elementCount = typeCount;

                        bufferSize = elementSize * elementCount;
                        break;
                    case 2:
                        if (counts.Length < 2)
                            return;

                        typeCount = counts[0];

                        elementCount = counts[1];

                        bufferSize = elementSize * elementCount;
                        break;
                    default:
                        if (counts.Length < 2)
                            return;

                        typeCount = counts[0];

                        bufferSize = counts[1];

                        break;
                }

                //int bufferSize = elementSize * elementCount;

                bufferSize = *this.bufferSize += bufferSize;
                typeCount = *this.typeCount += typeCount;

                writer._Reset(bufferSize, typeCount);
            }
        }

        internal struct Command
        {
            public enum Type
            {
                ComponentData,
                BufferOverride,
                BufferAppend,
                BufferAppendUnique,
                BufferRemove,
                BufferRemoveSwapBack, 
                Enable, 
                Disable
            }

            public static Type GetBufferType(BufferOption option)
            {
                return (Type)((int)option + (int)Type.BufferOverride);
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

        internal struct Value : IComparable<Value>
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

        internal struct Info
        {
            public UnsafeBuffer buffer;
            public UnsafeParallelMultiHashMap<Entity, TypeIndex> entityTypes;
            public UnsafeParallelMultiHashMap<Key, Value> values;

            /*#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        internal AtomicSafetyHandle m_Safety;

                        //internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<Data>();
            #endif*/

            public AllocatorManager.AllocatorHandle allocator => buffer.allocator;

            public Info(in AllocatorManager.AllocatorHandle allocator)
            {
                buffer = new UnsafeBuffer(0, 1, allocator);
                entityTypes = new UnsafeParallelMultiHashMap<Entity, TypeIndex>(1, allocator);
                values = new UnsafeParallelMultiHashMap<Key, Value>(1, allocator);
            }

            public void Dispose()
            {
                buffer.Dispose();
                entityTypes.Dispose();
                values.Dispose();
            }

            public void Clear()
            {
                entityTypes.Clear();
                values.Clear();
                buffer.Reset();
            }

            public void Reset(int bufferSize, int typeCount)
            {
                int length = buffer.position + bufferSize;
                buffer.capacity = math.max(buffer.capacity, length);
                buffer.length = math.max(buffer.length, length);

                entityTypes.Capacity = math.max(entityTypes.Capacity, entityTypes.Count() + typeCount);
                values.Capacity = math.max(values.Capacity, values.Count() + typeCount);
            }

            public bool IsComponentEnabled(in Entity entity, in TypeIndex typeIndex)
            {
                bool result = false;
                int index = -1;

                Key key;
                key.entity = entity;
                key.typeIndex = typeIndex;

                if (values.TryGetFirstValue(key, out var temp, out var iterator))
                {
                    do
                    {
                        switch(temp.command.type)
                        {
                            case Command.Type.Enable:
                                if(temp.index > index)
                                {
                                    index = temp.index;

                                    result = true;
                                }
                                break;
                            case Command.Type.Disable:
                                if (temp.index > index)
                                {
                                    index = temp.index;

                                    result = false;
                                }
                                break;
                        }

                    } while (values.TryGetNextValue(out temp, ref iterator));
                }

                return result;
            }

            public bool TryGetComponentData<T>(in Entity entity, ref T value) where T : struct, IComponentData
            {
                int index = -1;

                Key key;
                key.entity = entity;
                key.typeIndex = TypeManager.GetTypeIndex<T>();

                if (values.TryGetFirstValue(key, out var temp, out var iterator))
                {
                    do
                    {
                        if (temp.command.type == Command.Type.ComponentData && temp.index > index)
                        {
                            index = temp.index;

                            value = temp.command.block.As<T>();
                        }
                    } while (values.TryGetNextValue(out temp, ref iterator));
                }

                return index != -1;
            }

            public bool TryGetBuffer<TValue, TList, TWrapper>(in Entity entity, ref TList list, ref TWrapper wrapper)
                where TValue : struct, IBufferElementData
                where TWrapper : IWriteOnlyListWrapper<TValue, TList>
            {
                Key key;
                key.entity = entity;
                key.typeIndex = TypeManager.GetTypeIndex<TValue>();

                int count = values.CountValuesForKey(key);
                if (count < 1)
                    return false;

                var commands = new NativeArray<Command>(count, Allocator.Temp);
                {
                    if (values.TryGetFirstValue(key, out var value, out var iterator))
                    {
                        do
                        {
                            commands[value.index] = value.command;
                        } while (values.TryGetNextValue(out value, ref iterator));
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
                            case Command.Type.BufferAppendUnique:
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

            public bool TryGetBuffer<T>(in Entity entity, int index, ref T value, int indexOffset = 0)
                where T : struct, IBufferElementData
            {
                bool result = false;

                Key key;
                key.entity = entity;
                key.typeIndex = TypeManager.GetTypeIndex<T>();

                int count = values.CountValuesForKey(key);
                if (count > 0)
                {
                    var commands = new NativeArray<Command>(count, Allocator.Temp);
                    {
                        if (values.TryGetFirstValue(key, out var temp, out var iterator))
                        {
                            do
                            {
                                commands[temp.index] = temp.command;
                            } while (values.TryGetNextValue(out temp, ref iterator));
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
                                case Command.Type.BufferAppendUnique:
                                    indexOffset += length;
                                    break;
                                default:
                                    break;
                            }

                            if (indexOffset > index)
                            {
                                resultIndex = length - indexOffset + index;
                                if (resultIndex >= 0 && resultIndex < length)
                                {
                                    value = array[resultIndex];

                                    result = true;
                                }
                            }
                        }
                    }
                    commands.Dispose();
                }

                return result;
            }

            public void SetComponentData<T>(in TypeIndex typeIndex, in Entity entity, in T value) where T : struct
            {
                UnityEngine.Assertions.Assert.IsTrue(typeIndex.IsComponentType);
                __CheckTypeSize<T>(typeIndex);

                Command command;
                command.type = Command.Type.ComponentData;
                command.block = buffer.writer.WriteBlock(UnsafeUtility.SizeOf<T>(), false);
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

            public unsafe void SetBuffer<T>(BufferOption option, in Entity entity, void* values, int length)
                where T : struct, IBufferElementData
            {
                Command command;
                command.type = Command.GetBufferType(option);
                if (values == null || length < 1)
                    command.block = UnsafeBlock.Empty;
                else
                {
                    int size = UnsafeUtility.SizeOf<T>() * length;

                    command.block = buffer.writer.WriteBlock(size, false);
                    command.block.writer.Write(values, size);
                }

                __Set<T>(entity, command);
            }

            public unsafe void SetBuffer<TValue, TCollection>(BufferOption option, in TypeIndex typeIndex, in Entity entity, in TCollection values)
                where TValue : struct
                where TCollection : IReadOnlyCollection<TValue>
            {
                UnityEngine.Assertions.Assert.IsTrue(typeIndex.IsBuffer);
                __CheckTypeSize<TValue>(typeIndex);

                Command command;
                command.type = Command.GetBufferType(option);

                int count = values == null ? 0 : values.Count;
                if (count > 0)
                {
                    command.block = buffer.writer.WriteBlock(UnsafeUtility.SizeOf<TValue>() * count, false);

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

                __Set(typeIndex, entity, command);
            }

            public unsafe void SetBuffer<TValue, TCollection>(BufferOption option, in Entity entity, in TCollection values)
                where TValue : struct, IBufferElementData
                where TCollection : IReadOnlyCollection<TValue>
            {
                SetBuffer<TValue, TCollection>(option, TypeManager.GetTypeIndex<TValue>(), entity, values);
            }

            public void SetComponentEnabled<T>(in Entity entity, bool value) where T : struct, IEnableableComponent
            {
                Command command;
                command.type = value ? Command.Type.Enable : Command.Type.Disable;
                command.block = UnsafeBlock.Empty;

                __Set<T>(entity, command);
            }

            public bool RemoveComponent(in Entity entity, in TypeIndex typeIndex)
            {
                Key key;
                key.typeIndex = typeIndex;
                key.entity = entity;
                if (values.Remove(key) > 0)
                {
                    if (entityTypes.TryGetFirstValue(entity, out var temp, out var iterator))
                    {
                        do
                        {
                            if (temp == key.typeIndex)
                            {
                                entityTypes.Remove(iterator);

                                return true;
                            }
                        } while (entityTypes.TryGetNextValue(out temp, ref iterator));
                    }

                    __CheckEntityType();
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

            internal void _Set(in Key key, in Command command)
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

            private void __Set<T>(in Entity entity, in Command command) where T : struct
            {
                Key key;
                key.entity = entity;
                key.typeIndex = TypeManager.GetTypeIndex<T>();

                _Set(key, command);
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

                _Set(key, command);
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            static void __CheckTypeSize<T>(int typeIndex) where T : struct
            {
                if (UnsafeUtility.SizeOf<T>() != TypeManager.GetTypeInfo(typeIndex).ElementSize)
                    throw new System.InvalidCastException();
            }

        }

        internal struct Data
        {
            public Info info;

            public JobHandle jobHandle;

            public int bufferSize;
            public int typeCount;

            public AllocatorManager.AllocatorHandle allocator => info.allocator;

            public Data(in AllocatorManager.AllocatorHandle allocator)
            {
                info = new Info(allocator);

                jobHandle = default;

                bufferSize = 0;
                typeCount = 0;
            }

            public void Dispose()
            {
                info.Dispose();
            }
        }

        [NativeContainer]
        internal struct Container
        {
            [NativeDisableUnsafePtrRestriction]
            internal unsafe Info* _info;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;

            internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<Container>();
#endif

            /*internal Data data
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
            }*/

            internal unsafe Container(ref EntityComponentAssigner assigner)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = assigner.m_Safety;

                CollectionHelper.SetStaticSafetyId<Container>(ref m_Safety, ref StaticSafetyID.Data);
#endif

                _info = (Info*)UnsafeUtility.AddressOf(ref assigner.__data->info);
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

            public unsafe void Clear()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
                _info->Clear();
            }

            public unsafe void Reset(int bufferSize, int typeCount)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

                _info->Reset(bufferSize, typeCount);
            }

            public void Reset(int elementSize, int elementCount, int typeCount)
            {
                Reset(elementSize * elementCount, typeCount);
            }

            public unsafe bool IsComponentEnabled(in Entity entity, in TypeIndex typeIndex)
            {
                __CheckRead();

                return _info->IsComponentEnabled(entity, typeIndex);
            }

            public unsafe bool TryGetComponentData<T>(in Entity entity, ref T value) where T : struct, IComponentData
            {
                __CheckRead();

                return _info->TryGetComponentData(entity, ref value);
            }

            public unsafe bool TryGetBuffer<TValue, TList, TWrapper>(in Entity entity, ref TList list, ref TWrapper wrapper)
                where TValue : struct, IBufferElementData
                where TWrapper : IWriteOnlyListWrapper<TValue, TList>
            {
                __CheckRead();

                return _info->TryGetBuffer< TValue, TList, TWrapper>(entity, ref list, ref wrapper);
            }

            public unsafe bool TryGetBuffer<T>(in Entity entity, int index, ref T value, int indexOffset = 0)
                where T : struct, IBufferElementData
            {
                __CheckRead();

                return _info->TryGetBuffer(entity, index, ref value, indexOffset);
            }

            public unsafe void SetComponentData<T>(in TypeIndex typeIndex, in Entity entity, in T value) where T : struct
            {
                __CheckWrite();

                _info->SetComponentData(typeIndex, entity, value);
            }

            public unsafe void SetComponentData<T>(in Entity entity, in T value) where T : struct, IComponentData
            {
                /*__CheckWrite();

                Command command;
                command.type = Command.Type.ComponentData;
                command.block = __buffer.writer.WriteBlock(UnsafeUtility.SizeOf<T>(), false);
                command.block.As<T>() = value;

                __Set<T>(entity, command);*/

                SetComponentData(TypeManager.GetTypeIndex<T>(), entity, value);
            }

            public unsafe void SetBuffer<T>(BufferOption option, in Entity entity, void* values, int length)
                where T : struct, IBufferElementData
            {
                __CheckWrite();

                _info->SetBuffer<T>(option, entity, values, length);
            }

            public unsafe void SetBuffer<T>(BufferOption option, in Entity entity, in NativeArray<T> values) where T : struct, IBufferElementData => SetBuffer<T>(
                option, entity, values.GetUnsafeReadOnlyPtr(), values.Length);

            public unsafe void SetBuffer<T>(BufferOption option, in Entity entity, in T[] values) where T : unmanaged, IBufferElementData
            {
                if (values == null)
                {
                    SetBuffer<T>(option, entity, null, 0);

                    return;
                }

                fixed (void* ptr = values)
                {
                    SetBuffer<T>(option, entity, ptr, values.Length);
                }
            }

            public unsafe void SetBuffer<TValue, TCollection>(BufferOption option, in TypeIndex typeIndex, in Entity entity, in TCollection values)
                where TValue : struct
                where TCollection : IReadOnlyCollection<TValue>
            {
                __CheckWrite();

                _info->SetBuffer<TValue, TCollection>(option, typeIndex, entity, values);
            }

            public unsafe void SetBuffer<TValue, TCollection>(BufferOption option, in Entity entity, in TCollection values)
                where TValue : struct, IBufferElementData
                where TCollection : IReadOnlyCollection<TValue>
            {
                __CheckWrite();

                _info->SetBuffer<TValue, TCollection>(option, entity, values);
            }

            public unsafe void SetComponentEnabled<T>(in Entity entity, bool value) where T : struct, IEnableableComponent
            {
                __CheckWrite();

                _info->SetComponentEnabled<T>(entity, value);
            }

            public unsafe bool RemoveComponent<T>(in Entity entity)
            {
                __CheckWrite();

                return _info->RemoveComponent(entity, TypeManager.GetTypeIndex<T>());
            }

            public unsafe bool Apply(
                ref SystemState systemState,
                in SharedHashMap<Entity, Entity>.Reader entities,
                SharedHashMap<Entity, Entity> wrapper,
                int innerloopBatchCount = 1)
            {
                if (_info->entityTypes.IsEmpty)
                    return false;

                AssignJob assign;
                assign.types = default;
                assign.entityStorageInfoLookup = systemState.GetEntityStorageInfoLookup();
                assign.wrapper = wrapper.isCreated ? wrapper.reader : default;
                assign.container = new ReadOnly(this);

                var jobHandle = wrapper.isCreated ? JobHandle.CombineDependencies(systemState.Dependency, wrapper.lookupJobManager.readOnlyJobHandle) : systemState.Dependency;
                var keys = _info->entityTypes.GetKeyArray(systemState.WorldUpdateAllocator);
                {
                    NativeParallelMultiHashMapIterator<Entity> iterator;

                    ComponentType componentType;
                    componentType.AccessModeType = ComponentType.AccessMode.ReadWrite;

                    int numTypes = 0, numEntities = keys.ConvertToUniqueArray(), entityIndex = 0, i;

                    assign.typeHandleIndicess = new UnsafeHashMap<int, int>(BurstCompatibleTypeArray.LENGTH, Allocator.TempJob);

                    Entity key, entity;
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

                        if (_info->entityTypes.TryGetFirstValue(key, out componentType.TypeIndex, out iterator))
                        {
                            do
                            {
                                if (assign.typeHandleIndicess.ContainsKey(componentType.TypeIndex))
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

                                    jobHandle = assign.typeHandleIndicess.Dispose(jobHandle);

                                    assign.typeHandleIndicess = new UnsafeHashMap<int, int>(BurstCompatibleTypeArray.LENGTH, Allocator.TempJob);

                                    break;
                                }

                                assign.typeHandleIndicess[componentType.TypeIndex] = numTypes;

                                assign.types[numTypes++] = systemState.GetDynamicComponentTypeHandle(componentType);
                            } while (_info->entityTypes.TryGetNextValue(out componentType.TypeIndex, ref iterator));
                        }
                    }

                    if (numTypes > 0)
                    {
                        assign.entityArray = keys.GetSubArray(entityIndex, numEntities - entityIndex);
                        jobHandle = assign.ScheduleByRef(assign.entityArray.Length, innerloopBatchCount, jobHandle);
                    }

                    jobHandle = assign.typeHandleIndicess.Dispose(jobHandle);
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

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void __CheckRead()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
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
        [NativeContainerIsReadOnly]
        public struct ReadOnly
        {
            [NativeDisableUnsafePtrRestriction]
            private unsafe Info* __info;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            internal unsafe ReadOnly(in Container container)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                //AtomicSafetyHandle.CheckReadAndThrow(data.m_Safety);

                m_Safety = container.m_Safety;

                AtomicSafetyHandle.UseSecondaryVersion(ref m_Safety);

                //UnityEngine.Debug.LogError($"dddd {AtomicSafetyHandle.GetAllowReadOrWriteAccess(m_Safety)}");
#endif

                __info = container._info;
            }

            public bool AppendTo(ref Writer assigner, in NativeArray<Entity> entityArray = default)
            {
                return AppendTo(ref assigner._container, entityArray);
            }

            internal unsafe bool AppendTo(ref Container container, in NativeArray<Entity> entityArray = default)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                if (__info->values.IsEmpty)
                    return false;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(container.m_Safety);
#endif

                bool result = false;
                using (var keys = __info->values.GetKeyArray(Allocator.Temp))
                {
                    var values = new NativeList<Value>(Allocator.Temp);

                    UnsafeBuffer.Writer writer = container._info->buffer.writer;
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

                        if (__info->values.TryGetFirstValue(key, out value, out iterator))
                        {
                            result = true;

                            values.Clear();

                            do
                            {
                                values.Add(value);
                            } while (__info->values.TryGetNextValue(out value, ref iterator));

                            values.Sort();

                            numValues = values.Length;
                            for (j = 0; j < numValues; ++j)
                            {
                                ref var source = ref values.ElementAt(j).command;

                                destination.type = source.type;

                                destination.block = source.block.isCreated ? writer.WriteBlock(source.block) : UnsafeBlock.Empty;

                                container._info->_Set(key, destination);
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
                in UnsafeHashMap<int, int> typeHandleIndicess,
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
                int blockSize, numValues, elementSize, i;

                key.entity = wrapper.isCreated ? wrapper[entity] : entity;

                if (__info->entityTypes.TryGetFirstValue(key.entity, out key.typeIndex, out entityIterator))
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
                        else if (TypeManager.IsZeroSized(key.typeIndex))
                            destination = null;
                        else
                            destination = (byte*)entityStorageInfo.Chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref dynamicComponentTypeHandle, elementSize).GetUnsafePtr() + entityStorageInfo.IndexInChunk * elementSize;

                        values.Clear();

                        if (__info->values.TryGetFirstValue(key, out value, out keyIterator))
                        {
                            do
                            {
                                values.Add(value);
                            } while (__info->values.TryGetNextValue(out value, ref keyIterator));
                        }

                        values.Sort();

                        numValues = values.Length;
                        for (i = 0; i < numValues; ++i)
                        {
                            command = values.ElementAt(i).command;
                            if (command.block.isCreated)
                                source = command.block.GetRangePtr(out blockSize);
                            else
                            {
                                source = null;

                                blockSize = 0;
                            }

                            /*if (TypeManager.GetTypeName(key.typeIndex)->ToString() == "NetworkIdentity")
                            {
                                UnityEngine.Debug.Log($"Assign {entity} : {*(uint*)source}");
                            }*/

                            switch (command.type)
                            {
                                case Command.Type.ComponentData:
                                    /*if (TypeManager.GetTypeName(key.typeIndex)->ToString().Contains("GameNodeCharacterStatus"))
                                        UnityEngine.Debug.LogError($"{entity} To {*(int*)source}");*/

                                    UnityEngine.Assertions.Assert.AreEqual(blockSize, elementSize);

                                    //destination = (byte*)batchInChunk.GetDynamicComponentDataArrayReinterpret<byte>(dynamicComponentTypeHandle, blockSize).GetUnsafePtr() + i * blockSize;
                                    break;
                                case Command.Type.BufferOverride:
                                case Command.Type.BufferAppend:
                                case Command.Type.BufferAppendUnique:
                                    if (source == null)
                                        bufferAccessor.ResizeUninitialized(entityStorageInfo.IndexInChunk, 0);
                                    else
                                    {
                                        int elementCount = blockSize / elementSize;
                                        //var bufferAccessor = batchInChunk.GetUntypedBufferAccessor(ref dynamicComponentTypeHandle);
                                        if (command.type == Command.Type.BufferOverride)
                                        {
                                            bufferAccessor.ResizeUninitialized(entityStorageInfo.IndexInChunk, elementCount);
                                            destination = bufferAccessor.GetUnsafePtr(entityStorageInfo.IndexInChunk);
                                        }
                                        else
                                        {
                                            byte* ptr;
                                            int originCount = bufferAccessor.GetBufferLength(entityStorageInfo.IndexInChunk);
                                            if (originCount > 0 && command.type == Command.Type.BufferAppendUnique)
                                            {
                                                ptr = (byte*)bufferAccessor.GetUnsafePtr(entityStorageInfo.IndexInChunk);
                                                byte* sourcePtr = (byte*)source, destinationPtr;
                                                int j, k;
                                                for (j = 0; j < elementCount; ++j)
                                                {
                                                    destinationPtr = ptr;
                                                    for (k = 0; k < originCount; ++k)
                                                    {
                                                        if (UnsafeUtility.MemCmp(destinationPtr, sourcePtr, elementSize) == 0)
                                                            break;

                                                        destinationPtr += elementSize;
                                                    }

                                                    if (k < originCount && j < --elementCount)
                                                        UnsafeUtility.MemMove(sourcePtr, sourcePtr + elementSize, (elementCount - j--) * elementSize);

                                                    sourcePtr += elementSize;
                                                }

                                                blockSize = elementCount * elementSize;
                                            }

                                            bufferAccessor.ResizeUninitialized(entityStorageInfo.IndexInChunk, originCount + elementCount);
                                            if (elementCount > 0)
                                            {
                                                ptr = (byte*)bufferAccessor.GetUnsafePtr(entityStorageInfo.IndexInChunk);
                                                destination = ptr + originCount * elementSize;
                                            }
                                            else
                                                source = null;
                                        }
                                    }
                                    break;
                                case Command.Type.BufferRemove:
                                case Command.Type.BufferRemoveSwapBack:
                                    if (source != null)
                                    {
                                        int bufferLength = bufferAccessor.GetBufferLength(entityStorageInfo.IndexInChunk);
                                        if (bufferLength > 0)
                                        {
                                            byte* ptr = (byte*)bufferAccessor.GetUnsafePtr(entityStorageInfo.IndexInChunk), sourcePtr = (byte*)source, destinationPtr;
                                            int j, k, elementCount = blockSize / elementSize;
                                            for (j = 0; j < elementCount; ++j)
                                            {
                                                destinationPtr = ptr;
                                                for (k = 0; k < bufferLength; ++k)
                                                {
                                                    if (UnsafeUtility.MemCmp(destinationPtr, sourcePtr, elementSize) == 0)
                                                    {
                                                        if (k < --bufferLength)
                                                        {
                                                            if (command.type == Command.Type.BufferRemoveSwapBack)
                                                                UnsafeUtility.MemCpy(destinationPtr, destinationPtr + elementSize * bufferLength, elementSize);
                                                            else
                                                                UnsafeUtility.MemMove(destinationPtr, destinationPtr + elementSize, (bufferLength - k) * elementSize);
                                                        }

                                                        break;
                                                    }

                                                    destinationPtr += elementSize;
                                                }

                                                if (bufferLength < 1)
                                                    break;

                                                sourcePtr += elementSize;
                                            }

                                            bufferAccessor.ResizeUninitialized(entityStorageInfo.IndexInChunk, bufferLength);
                                        }

                                        source = null;
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
                    } while (__info->entityTypes.TryGetNextValue(out key.typeIndex, ref entityIterator));
                }

                values.Dispose();
            }
        }

        public struct Writer
        {
            internal Container _container;

            internal Writer(ref Container container)
            {
                _container = container;

/*#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.UseSecondaryVersion(ref __container.m_Safety);
#endif*/
            }

            public void SetComponentData<T>(in TypeIndex typeIndex, in Entity entity, in T value) where T : struct => _container.SetComponentData(typeIndex, entity, value);

            public void SetComponentData<T>(in Entity entity, in T value) where T : struct, IComponentData => _container.SetComponentData(entity, value);

            public unsafe void SetBuffer<T>(BufferOption option, in Entity entity, void* values, int length)
                where T : struct, IBufferElementData => _container.SetBuffer<T>(option, entity, values, length);

            public unsafe void SetBuffer<T>(BufferOption option, in Entity entity, in NativeArray<T> values) where T : struct, IBufferElementData =>
                _container.SetBuffer(option, entity, values);

            public void Clear()
            {
                _container.Clear();
            }

            internal void _Reset(int bufferSize, int typeCount) => _container.Reset(bufferSize, typeCount);
        }

        [NativeContainer, NativeContainerIsAtomicWriteOnly]
        public struct ParallelWriter
        {
            private UnsafeBuffer.ParallelWriter __buffer;
            private UnsafeParallelMultiHashMap<Entity, TypeIndex>.ParallelWriter __entityTypes;
            private UnsafeParallelMultiHashMap<Key, Value>.ParallelWriter __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            internal unsafe ParallelWriter(ref Container container)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = container.m_Safety;

                AtomicSafetyHandle.UseSecondaryVersion(ref m_Safety);
#endif

                __buffer = container._info->buffer.parallelWriter;
                __entityTypes = container._info->entityTypes.AsParallelWriter();
                __values = container._info->values.AsParallelWriter();
            }

            public void SetComponentData<T>(in TypeIndex typeIndex, in Entity entity, in T value) where T : struct
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

            public unsafe void AppendBuffer<T>(in TypeIndex typeIndex, in Entity entity, void* values, int length) where T : struct
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

            public unsafe void AppendBuffer<T>(in TypeIndex typeIndex, in Entity entity, ref T value) where T : struct, IBufferElementData => AppendBuffer<T>(typeIndex, entity, UnsafeUtility.AddressOf(ref value), 1);

            public unsafe void AppendBuffer<T>(in TypeIndex typeIndex, in Entity entity, NativeArray<T> values) where T : struct, IBufferElementData => AppendBuffer<T>(typeIndex, entity, NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(values), values.Length);

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
            static void __CheckTypeSize<T>(in TypeIndex typeIndex) where T : struct
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

        private unsafe Data* __data;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<EntityComponentAssigner>();
#endif

        public unsafe bool isCreated => __data != null;

        public unsafe int bufferSize
        {
            get => __data->bufferSize;

            private set
            {
                __data->bufferSize = value;
            }
        }

        public unsafe int typeCount
        {
            get => __data->typeCount;

            private set
            {
                __data->typeCount = value;
            }
        }

        public unsafe JobHandle jobHandle
        {
            get => __data->jobHandle;

            set => __data->jobHandle = value;
        }

        public Writer writer
        {
            get
            {
                var container = this.container;
                return new Writer(ref container);
            }
        }

        internal Container container => new Container(ref this);

        public unsafe EntityComponentAssigner(in AllocatorManager.AllocatorHandle allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator);

            CollectionHelper.SetStaticSafetyId<EntityComponentAssigner>(ref m_Safety, ref StaticSafetyID.Data);
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif

            __data = AllocatorManager.Allocate<Data>(allocator);
            *__data = new Data(allocator);
        }

        public unsafe void Dispose()
        {
            jobHandle.Complete();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);
            AtomicSafetyHandle.Release(m_Safety);
#endif

            var allocator = __data->allocator;

            __data->Dispose();

            AllocatorManager.Free(allocator, __data);
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
            return new ReadOnly(container);
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

            var container = this.container;
            return new ParallelWriter(ref container);
        }

        public unsafe ParallelWriter AsParallelWriter(in NativeArray<int> typeCountAndBufferSize, ref JobHandle jobHandle)
        {
            ResizeJob resize;
            resize.elementSize = 0;
            resize.counterLength = 0;
            resize.counts = typeCountAndBufferSize;
            resize.bufferSize = (int*)UnsafeUtility.AddressOf(ref __data->bufferSize);
            resize.typeCount = (int*)UnsafeUtility.AddressOf(ref __data->typeCount);
            resize.writer = writer;
            jobHandle = resize.Schedule(JobHandle.CombineDependencies(jobHandle, this.jobHandle));

            this.jobHandle = jobHandle;

            var container = this.container;
            return new ParallelWriter(ref container);
        }

        public ParallelWriter AsComponentDataParallelWriter<T>(int entityCount) where T : struct, IComponentData
        {
            return AsParallelWriter(UnsafeUtility.SizeOf<T>() * entityCount, entityCount);
        }

        public ParallelWriter AsBufferParallelWriter<T>(int elementCount, int typeCount) where T : struct, IBufferElementData
        {
            return AsParallelWriter(UnsafeUtility.SizeOf<T>() * elementCount, typeCount);
        }

        public unsafe ParallelWriter AsComponentDataParallelWriter<T>(in NativeArray<int> entityCount, ref JobHandle jobHandle) where T  : struct, IComponentData
        {
            ResizeJob resize;
            resize.elementSize = UnsafeUtility.SizeOf<T>();
            resize.counterLength = 1;
            resize.counts = entityCount;
            resize.bufferSize = (int*)UnsafeUtility.AddressOf(ref __data->bufferSize);
            resize.typeCount = (int*)UnsafeUtility.AddressOf(ref __data->typeCount);
            resize.writer = writer;
            jobHandle = resize.ScheduleByRef(JobHandle.CombineDependencies(jobHandle, this.jobHandle));

            this.jobHandle = jobHandle;

            var container = this.container;
            return new ParallelWriter(ref container);
        }

        public unsafe ParallelWriter AsBufferParallelWriter<T>(in NativeArray<int> typeAndBufferCounts, ref JobHandle jobHandle) where T : struct, IBufferElementData
        {
            ResizeJob resize;
            resize.elementSize = UnsafeUtility.SizeOf<T>();
            resize.counterLength = 2;
            resize.counts = typeAndBufferCounts;
            resize.bufferSize = (int*)UnsafeUtility.AddressOf(ref __data->bufferSize);
            resize.typeCount = (int*)UnsafeUtility.AddressOf(ref __data->typeCount);
            resize.writer = writer;
            jobHandle = resize.ScheduleByRef(JobHandle.CombineDependencies(jobHandle, this.jobHandle));

            this.jobHandle = jobHandle;

            var container = this.container;
            return new ParallelWriter(ref container);
        }

        public bool IsComponentEnabled(in Entity entity, in TypeIndex typeIndex)
        {
            CompleteDependency();

            return container.IsComponentEnabled(entity, typeIndex);
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

        public void SetComponentData<T>(in TypeIndex typeIndex, in Entity entity, in T value) where T : struct
        {
            CompleteDependency();

            container.SetComponentData(typeIndex, entity, value);
        }

        public void SetComponentData<T>(in Entity entity, in T value) where T : struct, IComponentData
        {
            CompleteDependency();

            container.SetComponentData(entity, value);
        }

        public unsafe void SetBuffer<T>(BufferOption option, in Entity entity, void* values, int length)
            where T : struct, IBufferElementData
        {
            CompleteDependency();

            container.SetBuffer<T>(option, entity, values, length);
        }

        public unsafe void SetBuffer<T>(BufferOption option, in Entity entity, T value)
            where T : struct, IBufferElementData
        {
            SetBuffer<T>(option, entity, UnsafeUtility.AddressOf(ref value), 1);
        }

        public void SetBuffer<T>(BufferOption option, in Entity entity, in NativeArray<T> values)
            where T : struct, IBufferElementData
        {
            CompleteDependency();

            container.SetBuffer(option, entity, values);
        }

        public void SetBuffer<T>(BufferOption option, in Entity entity, in T[] values) where T : unmanaged, IBufferElementData
        {
            CompleteDependency();

            container.SetBuffer(option, entity, values);
        }

        public void SetBuffer<TValue, TCollection>(BufferOption option, in TypeIndex typeIndex, in Entity entity, in TCollection values)
            where TValue : struct
            where TCollection : IReadOnlyCollection<TValue>
        {
            CompleteDependency();

            container.SetBuffer<TValue, TCollection>(option, typeIndex, entity, values);
        }

        public void SetBuffer<TValue, TCollection>(BufferOption option, in Entity entity, in TCollection values)
            where TValue : struct, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            CompleteDependency();

            container.SetBuffer<TValue, TCollection>(option, entity, values);
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
            return (long)__data;
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
        private struct SharedComponent : IEquatable<SharedComponent>
        {
            public TypeIndex typeIndex;
            public UnsafeBlock value;

            public unsafe bool Equals(SharedComponent other)
            {
                return typeIndex == other.typeIndex &&
                    (!value.isCreated || 
                    !other.value.isCreated || 
                    TypeManager.SharedComponentEquals(value.GetRangePtr(out _), other.value.GetRangePtr(out _), typeIndex));
            }

            public override int GetHashCode()
            {
                return typeIndex.GetHashCode();
            }
        }

        private struct Data
        {
            private UnsafeBuffer __buffer;
            private UnsafeParallelMultiHashMap<SharedComponent, Entity> __entities;

            public AllocatorManager.AllocatorHandle allocator => __buffer.allocator;

            public Data(in AllocatorManager.AllocatorHandle allocator)
            {
                __buffer = new UnsafeBuffer(0, 1, allocator);
                __entities = new UnsafeParallelMultiHashMap<SharedComponent, Entity>(1, allocator);
            }

            public void Dispose()
            {
                __buffer.Dispose();
                __entities.Dispose();
            }

            public void Clear()
            {
                __buffer.Reset();
                __entities.Clear();
            }

            public bool TryGetSharedComponentData<T>(in Entity entity, ref T value) where T : struct, ISharedComponentData
            {
                SharedComponent sharedComponent;
                sharedComponent.typeIndex = TypeManager.GetTypeIndex<T>();
                sharedComponent.value = UnsafeBlock.Empty;

                if (__entities.TryGetFirstValue(sharedComponent, out var temp, out var iterator))
                {
                    do
                    {
                        if (temp == entity)
                        {
                            value = sharedComponent.value.As<T>();

                            return true;
                        }
                    } while (__entities.TryGetNextValue(out temp, ref iterator));
                }

                return false;
            }

            public void SetSharedComponent<T>(in Entity entity, in T value) where T : struct, ISharedComponentData
            {
                var typeIndex = TypeManager.GetTypeIndex<T>();

                RemoveComponent(entity, typeIndex);

                var writer = __buffer.writer;

                SharedComponent sharedComponent;
                sharedComponent.typeIndex = typeIndex;
                sharedComponent.value = (UnsafeBlock)writer.WriteBlock(value);

                __entities.Add(sharedComponent, entity);
            }

            public bool RemoveComponent(in Entity entity, in TypeIndex typeIndex)
            {
                SharedComponent sharedComponent;
                sharedComponent.typeIndex = typeIndex;
                sharedComponent.value = UnsafeBlock.Empty;

                if(__entities.TryGetFirstValue(sharedComponent, out var temp, out var iterator))
                {
                    do
                    {
                        if(temp == entity)
                        {
                            __entities.Remove(iterator);

                            return true;
                        }
                    } while (__entities.TryGetNextValue(out temp, ref iterator));
                }

                return false;
            }

            public unsafe bool Apply(ref EntityManager entityManager)
            {
                if (__entities.IsEmpty)
                    return false;

                using (var sharedComponents = __entities.GetKeyArray(Allocator.Temp))
                {
                    var entities = new NativeList<Entity>(Allocator.Temp);
                    foreach (var sharedComponent in sharedComponents)
                    {
                        entities.Clear();
                        foreach (var entity in __entities.GetValuesForKey(sharedComponent))
                            entities.Add(entity);

                        entityManager.SetSharedComponent(entities.AsArray(), sharedComponent.value.GetRangePtr(out _), sharedComponent.typeIndex);
                    }
                }

                return true;
            }
        }

        [NativeDisableUnsafePtrRestriction]
        private unsafe Data* __data;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<EntitySharedComponentAssigner>();
#endif

        public unsafe EntitySharedComponentAssigner(in AllocatorManager.AllocatorHandle allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator);

            CollectionHelper.SetStaticSafetyId<EntitySharedComponentAssigner>(ref m_Safety, ref StaticSafetyID.Data);
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif

            __data = AllocatorManager.Allocate<Data>(allocator);

            *__data = new Data(allocator);
        }

        public unsafe void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);
            AtomicSafetyHandle.Release(m_Safety);
#endif

            var allocator = __data->allocator;

            __data->Dispose();

            AllocatorManager.Free(allocator, __data);
        }

        public unsafe void Clear()
        {
            __CheckWrite();

            __data->Clear();
        }

        public unsafe bool TryGetSharedComponentData<T>(in Entity entity, ref T value) where T : struct, ISharedComponentData
        {
            __CheckRead();

            return __data->TryGetSharedComponentData(entity, ref value);
        }

        public unsafe void SetSharedComponentData<T>(in Entity entity, in T value) where T : struct, ISharedComponentData
        {
            __CheckWrite();

            __data->SetSharedComponent(entity, value);
        }

        public unsafe bool RemoveComponent<T>(in Entity entity)
        {
            __CheckWrite();

            return __data->RemoveComponent(entity, TypeManager.GetTypeIndex<T>());
        }

        public unsafe bool Apply(ref EntityManager entityManager)
        {
            __CheckRead();

            return __data->Apply(ref entityManager);
        }

        public unsafe void Playback(ref EntityManager entityManager)
        {
            __CheckWrite();

            if(__data->Apply(ref entityManager))
                __data->Clear();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void __CheckRead()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void __CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
        }
    }
}