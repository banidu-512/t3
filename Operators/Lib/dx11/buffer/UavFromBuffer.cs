namespace Lib.dx11.buffer;

[Guid("cc4847f8-a8a3-4da5-8b71-c4f3a3f488e6")]
public class UavFromBuffer : Instance<UavFromBuffer>
{
    [Output(Guid = "D7CF0DAE-FFB7-4408-A1EA-B0C1B4BC60C2")]
    public readonly Slot<UnorderedAccessView> UnorderedAccessView = new();

    public UavFromBuffer()
    {
        UnorderedAccessView.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var buffer = Buffer.GetValue(context);
        ResourceManager.CreateBufferUav<uint>(buffer, Format.R32_UInt, ref UnorderedAccessView.Value);
    }

    [Input(Guid = "58EBAE6E-7D8C-45A0-8266-8B71F601DA0A")]
    public readonly InputSlot<Buffer> Buffer = new();
}