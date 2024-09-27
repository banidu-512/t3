using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using T3.Core.Utils;

namespace lib.dx11.tex;

[Guid("c2078514-cf1d-439c-a732-0d7b31b5084a")]
public class SrvFromTexture2d : Instance<SrvFromTexture2d>
{
    [Output(Guid = "{DC71F39F-3FBA-4FC6-B8EF-CE57C82BF78E}")]
    public readonly Slot<ShaderResourceView> ShaderResourceView = new();

    public SrvFromTexture2d()
    {
        ShaderResourceView.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        try
        {
            Texture2D texture = Texture.GetValue(context);
            if (texture != null)
            {
                ShaderResourceView.Value?.Dispose();
                if ((texture.Description.BindFlags & BindFlags.DepthStencil) > 0)
                {
                    // it's a depth stencil texture, so we need to set the format explicitly
                    var desc = new ShaderResourceViewDescription()
                                   {
                                       Format = Format.R32_Float,
                                       Dimension = ShaderResourceViewDimension.Texture2D,
                                       Texture2D = new ShaderResourceViewDescription.Texture2DResource
                                                       {
                                                           MipLevels = 1,
                                                           MostDetailedMip = 0
                                                       }
                                   };
                    ShaderResourceView.Value = new ShaderResourceView(ResourceManager.Device, texture, desc);
                }
                else
                {
                    ShaderResourceView.Value = new ShaderResourceView(ResourceManager.Device, texture); // todo: create via resource manager
                }
            }
            else
            {
                Utilities.Dispose(ref ShaderResourceView.Value);
            }
            _complainedOnce = false;
        }
        catch (Exception e)
        {
            if(!_complainedOnce)
                Log.Error("Updating Shader Resource View failed: " + e.Message, this);

            _complainedOnce = true;
                
        }
    }

    private bool _complainedOnce = false;

    [Input(Guid = "{D5AFA102-2F88-431E-9CD4-AF91E41F88F6}")]
    public readonly InputSlot<Texture2D> Texture = new();
}