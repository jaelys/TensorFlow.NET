﻿using NumSharp;
using System.Collections.Generic;
using System.Linq;
using Tensorflow.Graphs;
using Tensorflow.Keras.ArgsDefinition;
using static Tensorflow.Binding;

namespace Tensorflow.Keras.Engine
{
    public class TensorFlowOpLayer : Layer
    {
        TensorFlowOpLayerArgs args;
        Dictionary<int, NDArray> constants => args.Constants;
        NodeDef node_def => args.NodeDef;
        static string TF_OP_LAYER_NAME_PREFIX = "tf_op_layer_";
        public string OpType => node_def.Op;

        public TensorFlowOpLayer(TensorFlowOpLayerArgs args)
            : base(new LayerArgs
            {
                Name = TF_OP_LAYER_NAME_PREFIX + args.Name,
                Trainable = args.Trainable,
                DType = args.DType,
                Autocast = false
            })
        {
            this.args = args;
            built = true;
        }

        protected override Tensors Call(Tensors inputs, Tensor state = null, bool is_training = false)
        {
            if (tf.Context.executing_eagerly())
                return _defun_call(inputs);
            return MakOp(inputs);
        }

        [AutoGraph]
        Tensors _defun_call(Tensors inputs)
            => MakOp(inputs);

        Tensors MakOp(Tensors inputs)
        {
            foreach (var (index, constant) in enumerate(constants))
            {
                var value = constant_op.constant(constant, name: node_def.Input[index]);
                var new_inputs = inputs.ToList();
                new_inputs.Insert(index, value);
                inputs = new Tensors(new_inputs.ToArray());
            }

            var graph = inputs.graph;
            var (c_op, _) = ops._create_c_op(graph, node_def, inputs.ToArray(), new Operation[0]);
            var op = graph._create_op_from_tf_operation(c_op);
            op._control_flow_post_processing();

            // Record the gradient because custom-made ops don't go through the
            // code-gen'd eager call path
            var op_type = op.node_def.Name;

            tf.Runner.RecordGradient(op_type, op.inputs._inputs, null, op.outputs);

            return op.output;
        }
    }
}
