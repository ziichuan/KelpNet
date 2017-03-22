﻿using System;
using System.Collections.Generic;

namespace KelpNet.Common.Functions
{
    //前回の入出力を自動的に扱うクラステンプレート
    [Serializable]
    public abstract class NeedPreviousDataFunction : Function
    {
        //後入れ先出しリスト
        private readonly List<BatchArray> _prevInput = new List<BatchArray>();
        private readonly List<BatchArray> _prevOutput = new List<BatchArray>();

        protected abstract BatchArray NeedPreviousForward(BatchArray x, bool isGpu);
        protected abstract BatchArray NeedPreviousBackward(BatchArray gy, BatchArray prevInput, BatchArray prevOutput, bool isGpu);

        protected NeedPreviousDataFunction(string name, int inputCount = 0, int oututCount = 0) : base(name, inputCount, oututCount)
        {
        }

        protected override BatchArray ForwardSingle(BatchArray x, bool isGpu)
        {
            this._prevInput.Add(x);

            BatchArray prevoutput = this.NeedPreviousForward(x, isGpu);
            this._prevOutput.Add(prevoutput);

            return prevoutput;
        }

        protected override BatchArray BackwardSingle(BatchArray gy, bool isGpu)
        {
            BatchArray prevInput = this._prevInput[this._prevInput.Count - 1];
            this._prevInput.RemoveAt(this._prevInput.Count - 1);

            BatchArray prevOutput = this._prevOutput[this._prevOutput.Count - 1];
            this._prevOutput.RemoveAt(this._prevOutput.Count - 1);

            return this.NeedPreviousBackward(gy, prevInput, prevOutput, isGpu);
        }

        public override BatchArray Predict(BatchArray x, bool isGpu = true)
        {
            return this.NeedPreviousForward(x, isGpu);
        }
    }
}
