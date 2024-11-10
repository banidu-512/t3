using System.Reflection;
using T3.Core.Utils;

namespace Lib.@string;

[Guid("82270977-07b5-4d86-8544-5aebc638d46c")]
internal sealed class CombineFields : Instance<CombineFields>, IGraphNodeOp
{
    [Output(Guid = "db0bbde0-18b6-4c53-8cf7-a294177d2089")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public CombineFields()
    {
        ShaderNode = new ShaderGraphNode(this, InputFields);
        Result.UpdateAction += Update;
        Result.Value = ShaderNode;
    }
    
    private void Update(EvaluationContext context)
    {
        ShaderNode.Update(context);
        
        // Get all parameters to clear operator dirty flag
        var combineMethod = CombineMethod.GetEnumValue<CombineMethods>(context);
        if(combineMethod != _combineMethod)
        {
            _combineMethod = combineMethod;
            ShaderNode.HasChangedCode = true;
        }
        
        InputFields.DirtyFlag.Clear();
    }

    public string GetShaderCode()
    {
        _callDef.Clear();
        var mode = _combineModes[(int)_combineMethod];
        
        _callDef.AppendLine("");
        _callDef.AppendLine($"#define {ShaderNode}CombineFunc(a,b) ({mode.Code})\n");
        _callDef.AppendLine($"float {ShaderNode}(float3 p) {{");
        _callDef.AppendLine($"    float d={mode.StartValue};" );
        
        foreach(var inputNode in ShaderNode.InputNodes) 
        {
            _callDef.AppendLine($"    d = {ShaderNode}CombineFunc(d,  {inputNode}(p));");
        }
        
        _callDef.AppendLine("    return d;");
        _callDef.AppendLine("}");
        return _callDef.ToString();
    }


    public ShaderGraphNode ShaderNode  { get; }

    private CombineMethods _combineMethod;
    private readonly StringBuilder _callDef = new();

    private sealed record CombineMethodDefs(string Code, float StartValue);
    
    private readonly CombineMethodDefs[] _combineModes=
        [
            new CombineMethodDefs("(a) + (b)",0), 
            new CombineMethodDefs("(a) - (b)", 0),
            new CombineMethodDefs("(a) * (b)", 1),
            new CombineMethodDefs("min(a, b)", 999999),            
            new CombineMethodDefs("max(a, b)", -999999),            
        ];
    
    private enum CombineMethods
    {
        Add,
        Sub,
        Multiply,
        Min,
        Max,
    }

    [Input(Guid = "7248C680-7279-4C1D-B968-3864CB849C77")]
    public readonly MultiInputSlot<ShaderGraphNode> InputFields = new();
        
    
    [Input(Guid = "4648E514-B48C-4A98-A728-3EBF9BCFA0B7", MappedType = typeof(CombineMethods))]
    public readonly InputSlot<int> CombineMethod = new();
}


