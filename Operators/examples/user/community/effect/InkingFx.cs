namespace Examples.user.community.effect;

[Guid("9484cda8-a5ab-414d-a5bd-9a413796c7ab")]
public class InkingFx : Instance<InkingFx>
{
    [Output(Guid = "f5f70a22-e07e-48d5-901a-2d88a6f5e1e0")]
    public readonly Slot<Texture2D> Output = new Slot<Texture2D>();


}