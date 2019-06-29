using ImGuiNET;
using imHelpers;
using System;
using System.Numerics;
using T3.Core.Operator;
using T3.Gui.TypeColors;

namespace T3.Gui.Graph
{
    /// <summary>
    /// Renders a graphic representation of a <see cref="SymbolChild"/> within the current <see cref="GraphCanvasWindow"/>
    /// </summary>
    static class GraphOperator
    {
        public static float _connectionZoneHeight = 6;
        public static Vector2 _labelPos = new Vector2(2, 2);

        public static void Draw(SymbolChildUi childUi)
        {
            ImGui.PushID(childUi.SymbolChild.Id.GetHashCode());
            {
                //var posInWindow = GraphCanvas.Current.ChildPosFromCanvas(childUi.PosOnCanvas + new Vector2(0, 3));
                //var posInApp = GraphCanvas.Current.TransformPosition(childUi.PosOnCanvas);

                var screenRect = GraphCanvas.Current.TransformRect(new ImRect(childUi.PosOnCanvas, childUi.PosOnCanvas + childUi.Size));

                // Interaction
                //ImGui.SetCursorPos(posInWindow);
                //ImGui.InvisibleButton("node", (childUi.Size - new Vector2(0, 6)) * GraphCanvas.Current.Scale);
                ImGui.SetCursorScreenPos(screenRect.Min);
                ImGui.InvisibleButton("node", screenRect.GetSize());

                THelpers.DebugItemRect();
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    T3UI.AddHoveredId(childUi.SymbolChild.Id);
                }

                if (ImGui.IsItemActive())
                {
                    if (ImGui.IsItemClicked(0))
                    {
                        if (!GraphCanvas.Current.SelectionHandler.SelectedElements.Contains(childUi))
                        {
                            GraphCanvas.Current.SelectionHandler.SetElement(childUi);
                        }
                    }
                    if (ImGui.IsMouseDragging(0))
                    {
                        foreach (var e in GraphCanvas.Current.SelectionHandler.SelectedElements)
                        {
                            e.PosOnCanvas += GraphCanvas.Current.InverseTransformDirection(ImGui.GetIO().MouseDelta);
                        }
                    }
                    if (ImGui.IsMouseDoubleClicked(0))
                    {
                        var instance = GraphCanvas.Current.CompositionOp.Children.Find(c => c.Symbol == childUi.SymbolChild.Symbol);
                        GraphCanvas.Current.CompositionOp = instance;
                    }
                }

                if (ImGui.IsItemHovered())
                {
                    NodeDetailsPanel.Draw(childUi);
                }

                // Rendering
                var dl = GraphCanvas.Current.DrawList;
                dl.ChannelsSplit(2);
                dl.ChannelsSetCurrent(1);

                dl.AddText(screenRect.Min + _labelPos, Color.White, string.Format($"{childUi.SymbolChild.ReadableName}"));
                dl.ChannelsSetCurrent(0);

                var hoveredFactor = T3UI.HoveredIdsLastFrame.Contains(childUi.SymbolChild.Id) ? 1.2f : 0.8f;

                //THelpers.OutlinedRect(ref dl, posInApp, childUi.Size * GraphCanvas.Current.Scale,
                //    fill: new Color(
                //            ((childUi.IsSelected || ImGui.IsItemHovered()) ? 0.3f : 0.2f) * hoveredFactor),
                //    outline: childUi.IsSelected ? Color.White : Color.Black);
                //dl.AddRectFilled(posInApp, posInApp +)
                dl.AddRectFilled(screenRect.Min, screenRect.Max, ColorVariations.OperatorBackground.GetVariation(Color.TRed));



                DrawSlots(childUi);

                dl.ChannelsMerge();
            }
            ImGui.PopID();


        }

        private static void DrawSlots(SymbolChildUi symbolChildUi)
        {
            for (int slot_idx = 0; slot_idx < symbolChildUi.SymbolChild.Symbol.OutputDefinitions.Count; slot_idx++)
            {
                Slots.DrawOutputSlot(symbolChildUi, slot_idx);
            }

            for (int slot_idx = 0; slot_idx < symbolChildUi.SymbolChild.Symbol.InputDefinitions.Count; slot_idx++)
            {
                Slots.DrawInputSlot(symbolChildUi, slot_idx);
            }
        }
    }
}
