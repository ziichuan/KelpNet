﻿using System;
using System.Collections.Generic;
using Cloo;
using KelpNet.Common;
using KelpNet.Common.Functions;

namespace KelpNet.Functions.Noise
{
    [Serializable]
    public class Dropout : Function
    {
        private readonly double dropoutRatio;
        private readonly List<double[]> maskStack = new List<double[]>();

        public Dropout(double dropoutRatio = 0.5, string name = "Dropout") : base(name)
        {
            this.dropoutRatio = dropoutRatio;

            //カーネルを作成
            if (IsGpu)
            {
                ForwardKernel = Weaver.CreateKernel(ForwardKernelSource, "DropoutForward");
                //BackwardKernel = Weaver.CreateKernel("", "");
            }
        }

        const string ForwardKernelSource =
@"
#pragma OPENCL EXTENSION cl_khr_fp64 : enable
__kernel void DropoutForward(
	__global const double *gpuX,
	__global const double *mask,
	__global double *gpuY,
    int maskLength)
{
	int i = get_global_id(0);

    gpuY[i] = gpuX[i] * mask[i % maskLength];
}";

        protected override BatchArray ForwardSingle(BatchArray x)
        {
            double[] result = new double[x.Data.Length];
            double[] mask = new double[x.Length];
            double scale = 1.0 / (1.0 - this.dropoutRatio);

            for (int i = 0; i < mask.Length; i++)
            {
                mask[i] = Mother.Dice.NextDouble() >= this.dropoutRatio ? scale : 0;
            }

            if (!IsGpu)
            {
                for (int i = 0; i < x.Data.Length; i++)
                {
                    result[i] = x.Data[i] * mask[i % mask.Length];
                }
            }
            else
            {
                using (ComputeBuffer<double> gpuX = new ComputeBuffer<double>(Weaver.Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, x.Data))
                using (ComputeBuffer<double> gpuMask = new ComputeBuffer<double>(Weaver.Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, mask))
                using (ComputeBuffer<double> gpuY = new ComputeBuffer<double>(Weaver.Context, ComputeMemoryFlags.WriteOnly | ComputeMemoryFlags.CopyHostPointer, result))
                {
                    ForwardKernel.SetMemoryArgument(0, gpuX);
                    ForwardKernel.SetMemoryArgument(1, gpuMask);
                    ForwardKernel.SetMemoryArgument(2, gpuY);
                    ForwardKernel.SetValueArgument(3, mask.Length);

                    Weaver.CommandQueue.Execute
                        (
                            ForwardKernel,
                            null,
                            new long[] { x.Data.Length },
                            null,
                            null
                        );

                    Weaver.CommandQueue.Finish();
                    Weaver.CommandQueue.ReadFromBuffer(gpuY, ref result, true, null);
                }
            }

            this.maskStack.Add(mask);

            return BatchArray.Convert(result, x.Shape, x.BatchCount);
        }

        protected override BatchArray BackwardSingle(BatchArray gy)
        {
            double[] result = new double[gy.Data.Length];
            double[] mask = this.maskStack[this.maskStack.Count - 1];
            this.maskStack.RemoveAt(this.maskStack.Count - 1);

            for (int b = 0; b < gy.BatchCount; b++)
            {
                for (int j = 0; j < mask.Length; j++)
                {
                    result[j + b * gy.Length] = gy.Data[j + b * gy.Length] * mask[j];
                }
            }

            return BatchArray.Convert(result, gy.Shape, gy.BatchCount);
        }

        //Predict時に何もしない
        public override BatchArray Predict(BatchArray input)
        {
            return input;
        }

    }
}
