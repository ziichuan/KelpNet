﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KelpNet.CPU;
using ProtoBuf;

#if DOUBLE
using KelpMath = System.Math;
#elif NETSTANDARD2_1
using KelpMath = System.MathF;
#elif NETSTANDARD2_0
using KelpMath = KelpNet.MathF;
#endif

namespace KelpNet.Tools
{
    public class OnnxmodelDataLoader
    {
        //binaryprotoを読み込む
        public static List<Function<T>> LoadNetWork<T>(string path) where T : unmanaged, IComparable<T>
        {
            List<Function<T>> result = new List<Function<T>>();

            using (FileStream stream = new FileStream(path, FileMode.Open))
            {
                //ONNXはパラメータ値がごちゃ混ぜで入ってくるため、そのカウンター
                int initilizerIndex = 0;

                ModelProto netparam = Serializer.Deserialize<ModelProto>(stream);

                List<TensorShapeProto.Dimension> dimension = netparam.Graph.Inputs[0].Type.TensorType.Shape.Dims;
                int[] inputShape = new int[dimension.Count]; //ここでは0次元目をバッチカウントとして扱う
                for (int i = 0; i < inputShape.Length; i++)
                {
                    inputShape[i] = (int)dimension[i].DimValue;
                }

                foreach (NodeProto node in netparam.Graph.Nodes)
                {
                    int[] outputShape;
                    result.Add(CreateFunction<T>(node, netparam.OpsetImports[0].Version, netparam.Graph.Initializers, inputShape, ref initilizerIndex, out outputShape));
                    inputShape = outputShape;
                }
            }

            return result;
        }

        static Function<T> CreateFunction<T>(NodeProto node, long version, List<TensorProto> initializers, int[] inputShape, ref int initilizerIndex, out int[] outputShape) where T : unmanaged, IComparable<T>
        {
            switch (node.OpType)
            {
                case "BatchNormalization":
                    if (version >= 9)
                    {
                        TensorProto bn_scale = initializers[initilizerIndex++];
                        TensorProto bn_b = initializers[initilizerIndex++];
                        TensorProto bn_mean = initializers[initilizerIndex++];
                        TensorProto bn_var = initializers[initilizerIndex++];

                        BatchNormalization<T> batchNormalization = new BatchNormalization<T>(
                            channelSize: bn_scale.FloatDatas.Length,
                            useGamma: true,
                            useBeta: true,
                            eps: node.GetAttribute("epsilon").F,
                            name: node.Name,
                            inputNames: new[] { node.Inputs[0] },
                            outputNames: new[] { node.Outputs[0] },
                            decay: node.GetAttribute("momentum").F
                        );

                        Array.Copy(bn_scale.FloatDatas, batchNormalization.Gamma.Data, bn_scale.FloatDatas.Length);
                        Array.Copy(bn_b.FloatDatas, batchNormalization.Beta.Data, bn_b.FloatDatas.Length);

                        Array.Copy(bn_mean.FloatDatas, batchNormalization.AvgMean.Data, bn_mean.FloatDatas.Length);
                        Array.Copy(bn_var.FloatDatas, batchNormalization.AvgVar.Data, bn_var.FloatDatas.Length);

                        outputShape = inputShape;
                        return batchNormalization;
                    }
                    else if (version >= 7)
                    {
                        TensorProto bn_scale = initializers[initilizerIndex++];
                        TensorProto bn_b = initializers[initilizerIndex++];
                        TensorProto bn_mean = initializers[initilizerIndex++];
                        TensorProto bn_var = initializers[initilizerIndex++];

                        //[spatial]
                        // If true, compute the mean and variance across per activation.
                        // If false, compute the mean and variance across per feature over each mini - batch.
                        // 真の場合は、活性化ごとに平均と分散を計算します。
                        // falseの場合は，ミニバッチごとに特徴量ごとの平均と分散を計算します．

                        int[] axis = null;
                        if (node.GetAttribute("spatial").I != 1)
                        {
                            List<int> tmp = new List<int>();
                            tmp.Add(0);//ここの次元指定はミニバッチ数に当たる
                            tmp.AddRange(Enumerable.Range(2, inputShape.Length - 2));
                            axis = tmp.ToArray();
                        }

                        BatchNormalization<T> batchNormalization = new BatchNormalization<T>(
                            channelSize: bn_scale.FloatDatas.Length,
                            eps: node.GetAttribute("epsilon").F,
                            name: node.Name,
                            inputNames: new[] { node.Inputs[0] },
                            outputNames: new[] { node.Outputs[0] },
                            decay: node.GetAttribute("momentum").F,
                            axis: axis
                        );

                        Array.Copy(bn_scale.FloatDatas, batchNormalization.Gamma.Data, bn_scale.FloatDatas.Length);
                        Array.Copy(bn_b.FloatDatas, batchNormalization.Beta.Data, bn_b.FloatDatas.Length);

                        Array.Copy(bn_mean.FloatDatas, batchNormalization.AvgMean.Data, bn_mean.FloatDatas.Length);
                        Array.Copy(bn_var.FloatDatas, batchNormalization.AvgVar.Data, bn_var.FloatDatas.Length);

                        outputShape = inputShape;
                        return batchNormalization;
                    }
                    else if (version >= 6)
                    {
                        //[spatial]
                        //If true, compute the mean and variance across all spatial elements.
                        //If false, compute the mean and variance across per feature.
                        //真の場合、すべての空間要素の平均と分散を計算します。
                        //偽の場合は，特徴量ごとの平均と分散を計算します。

                        throw new NotImplementedException();
                    }
                    else if (version >= 1)
                    {
                        throw new NotImplementedException();
                    }
                    break;

                case "Conv":
                    if (version >= 11)
                    {
                        throw new NotImplementedException();
                    }
                    else if (version >= 1)
                    {
                        TensorProto conv_w = initializers[initilizerIndex++];
                        TensorProto conv_b = null;

                        if (node.Inputs.Count > 2)
                        {
                            conv_b = initializers[initilizerIndex++];
                        }

                        outputShape = inputShape;
                        return new Convolution2D<T>(
                            inputChannels: (int)conv_w.Dims[1],
                            outputChannels: (int)conv_w.Dims[0],
                            kernelSize: Array.ConvertAll(node.GetAttribute("kernel_shape").Ints, s => (int)s),
                            stride: Array.ConvertAll(node.GetAttribute("strides").Ints, s => (int)s),
                            pad: Array.ConvertAll(node.GetAttribute("pads").Ints, s => (int)s), //pads: [x1_begin, x2_begin...x1_end, x2_end,...]で入ってくるので使用するのは前2つ
                            noBias: node.Inputs.Count < 3,
                            initialW: conv_w.FloatDatas,
                            initialb: conv_b?.FloatDatas,
                            name: node.Name,
                            inputNames: new[] { node.Inputs[0] },
                            outputNames: new[] { node.Outputs[0] });
                    }
                    break;

                case "Dropout":
                    if (version >= 12)
                    {
                        throw new NotImplementedException();
                    }
                    else if (version >= 10)
                    {
                        throw new NotImplementedException();
                    }
                    else if (version >= 7)
                    {
                        outputShape = inputShape;
                        return new Dropout<T>(node.GetAttribute("ratio").F, name: node.Name, inputNames: new[] { node.Inputs[0] }, outputNames: new[] { node.Outputs[0] });
                    }
                    else if (version >= 6)
                    {
                        throw new NotImplementedException();
                    }
                    else if (version >= 1)
                    {
                        throw new NotImplementedException();
                    }
                    break;

                case "Flatten":
                    outputShape = inputShape;//厳密には変わるが、関数内で吸収されるため不要
                    return null;

                case "Gemm":
                    if (version >= 11)
                    {
                        throw new NotImplementedException();
                    }
                    else if (version >= 9)
                    {
                        throw new NotImplementedException();
                    }
                    else if (version >= 7)
                    {
                        TensorProto w = initializers[initilizerIndex++];
                        TensorProto b = initializers[initilizerIndex++];

                        outputShape = new[]
                        {
                            inputShape[0],  //バッチカウント
                            (int)w.Dims[0]  //出力数
                        };

                        return new Linear<T>(
                            inputCount: (int)w.Dims[1],
                            outputCount: (int)w.Dims[0],
                            name: node.Name,
                            inputNames: new[] { node.Inputs[0] },
                            outputNames: new[] { node.Outputs[0] },
                            noBias: false,
                            initialW: w.FloatDatas,
                            initialb: b.FloatDatas
                        );
                    }
                    else if (version >= 6)
                    {
                        throw new NotImplementedException();
                    }
                    else if (version >= 1)
                    {
                        throw new NotImplementedException();
                    }
                    break;

                case "MaxPool":
                    if (version >= 12)
                    {
                        throw new NotImplementedException();
                    }
                    else if (version >= 11)
                    {
                        throw new NotImplementedException();
                    }
                    else if (version >= 10)
                    {
                        throw new NotImplementedException();
                    }
                    else if (version >= 8)
                    {
                        int[] kernelSize = Array.ConvertAll(node.GetAttribute("kernel_shape").Ints, s => (int)s);
                        int[] stride = Array.ConvertAll(node.GetAttribute("strides").Ints, s => (int)s);
                        int[] pad = Array.ConvertAll(node.GetAttribute("pads").Ints, s => (int)s);

                        List<int> tmpOutputShape = new List<int>();
                        tmpOutputShape.Add(inputShape[0]);//ミニバッチカウント
                        tmpOutputShape.Add(inputShape[1]);//チャンネル
                        tmpOutputShape.Add((int)KelpMath.Floor((inputShape[2] - kernelSize[1] + pad[1] * 2.0f + stride[1] - 1.0f) / stride[1]) + 1);
                        tmpOutputShape.Add((int)KelpMath.Floor((inputShape[3] - kernelSize[0] + pad[0] * 2.0f + stride[0] - 1.0f) / stride[0]) + 1);
                        outputShape = tmpOutputShape.ToArray();

                        return new MaxPooling2D<T>(
                            kernelSize: kernelSize,
                            stride: stride,
                            pad: pad,
                            name: node.Name,
                            inputNames: new[] { node.Inputs[0] },
                            outputNames: new[] { node.Outputs[0] }
                        );
                    }
                    else if (version >= 1)
                    {
                        throw new NotImplementedException();
                    }
                    break;

                case "Relu":
                    if (version >= 6)
                    {
                        outputShape = inputShape;
                        return new ReLU<T>(name: node.Name, inputNames: new[] { node.Inputs[0] }, outputNames: new[] { node.Outputs[0] });
                    }
                    else if (version >= 1)
                    {
                        throw new NotImplementedException();
                    }
                    break;
            }

            Console.WriteLine(node.OpType + "was not implemented.");
            throw new NotImplementedException();
        }
    }

    public static class TensorProtoConverter
    {
        public static AttributeProto GetAttribute(this NodeProto node, string str)
        {
            return node.Attributes.First(o => o.Name == str);
        }

        public static NdArray<T> ToNdArray<T>(this TensorProto tensorProto) where T : unmanaged, IComparable<T>
        {
            return new NdArray<T>(tensorProto.Dims);
        }
    }

}