using System;
using UnityEditor.Graphing;
using UnityEngine;

namespace UnityEditor.ShaderGraph.Internal
{
    [Serializable]
    [FormerName("UnityEditor.ShaderGraph.Vector2ShaderProperty")]
    [BlackboardInputInfo(1)]
    public sealed class Vector2ShaderProperty : VectorShaderProperty
    {
        internal Vector2ShaderProperty()
        {
            displayName = "Vector2";
        }

        public override PropertyType propertyType => PropertyType.Vector2;

        internal override AbstractMaterialNode ToConcreteNode()
        {
            var node = new Vector2Node();
            node.FindInputSlot<Vector1MaterialSlot>(Vector2Node.InputSlotXId).value = value.x;
            node.FindInputSlot<Vector1MaterialSlot>(Vector2Node.InputSlotYId).value = value.y;
            return node;
        }

        internal override PreviewProperty GetPreviewMaterialProperty()
        {
            return new PreviewProperty(propertyType)
            {
                name = referenceName,
                vector4Value = value
            };
        }

        internal override ShaderInput Copy()
        {
            return new Vector2ShaderProperty()
            {
                displayName = displayName,
                hidden = hidden,
                value = value,
                precision = precision,
                gpuInstanced = gpuInstanced,
            };
        }

        internal override void ForeachHLSLProperty(Action<HLSLProperty> action)
        {
            HLSLDeclaration decl = gpuInstanced ? HLSLDeclaration.HybridPerInstance :
                                    (generatePropertyBlock ? HLSLDeclaration.Global : HLSLDeclaration.UnityPerMaterial);
            if (m_generationType == HLSLDeclaration.None)
                decl = HLSLDeclaration.None;

            action(new HLSLProperty(HLSLType._float2, referenceName, decl, concretePrecision));
        }
    }
}
