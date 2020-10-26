﻿using ImGuiNET;
using T3.Core.Operator;
using T3.Gui.ChildUi.Animators;
using T3.Operators.Types.Id_23794a1f_372d_484b_ac31_9470d0e77819;
using UiHelpers;

namespace T3.Gui.ChildUi
{
    public static class Jitter2dUi
    {
        public static SymbolChildUi.CustomUiResult DrawChildUi(Instance instance, ImDrawListPtr drawList, ImRect screenRect)
        {
            if (!(instance is Jitter2d jitter2d)
                ||!ImGui.IsRectVisible(screenRect.Min, screenRect.Max))
            
                return SymbolChildUi.CustomUiResult.None;
            
            if (AnimatorLabel.Draw(ref jitter2d.Rate.TypedInputValue.Value, 
                               screenRect, drawList, nameof(jitter2d)))
            {
                jitter2d.Rate.Input.IsDefault = false;
                jitter2d.Rate.DirtyFlag.Invalidate();
            }
            var label = $"±{jitter2d.JumpDistance.TypedInputValue.Value:0.0}";
            
            if (MicroGraph.Draw(ref jitter2d.JumpDistance.TypedInputValue.Value, 
                                ref jitter2d.Blending.TypedInputValue.Value, 
                                jitter2d.Fragment, 
                                screenRect, drawList, label))
            {
                jitter2d.Blending.Input.IsDefault = false;
                jitter2d.Blending.DirtyFlag.Invalidate();
                jitter2d.JumpDistance.Input.IsDefault = false;
                jitter2d.JumpDistance.DirtyFlag.Invalidate();
            }
            return SymbolChildUi.CustomUiResult.Rendered  | SymbolChildUi.CustomUiResult.PreventInputLabels;
        }
    }
}