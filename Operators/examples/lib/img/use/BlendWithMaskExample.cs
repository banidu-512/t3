namespace Examples.lib.img.use;

[Guid("6594c457-82ab-4121-8e51-5212fe69262f")]
public class BlendWithMaskExample : Instance<BlendWithMaskExample>
{
    [Output(Guid = "90916b9d-a009-44a1-9888-94ca4ef0785c")]
    public readonly Slot<Texture2D> Output = new();


}