﻿#nullable enable
using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Editor.Gui.InputUi;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.Ui;

namespace T3.Editor.Gui.MagGraph.Interaction;

internal sealed partial class MagItemMovement
{
    [SuppressMessage("ReSharper", "NotAccessedField.Local")]
    private sealed class Snapping
    {
        public float BestDistance;
        public MagGraphItem? BestA;
        public MagGraphItem? BestB;
        public MagGraphItem.Directions Direction;
        public int InputLineIndex;
        public int MultiInputIndex;
        public int OutLineIndex;
        public Vector2 OutAnchorPos;
        public Vector2 InputAnchorPos;
        public bool Reverse;
        public bool IsSnapped => BestDistance < MagGraphItem.LineHeight * (IsInsertion ? 0.35 : 0.5f);
        public bool IsInsertion;
        public SplitInsertionPoint? InsertionPoint;

        public void TestItemsForSnap(MagGraphItem a, MagGraphItem b, bool revert, MagGraphCanvas canvas)
        {
            MagGraphConnection? inConnection;

            int aOutLineIndex, bInputLineIndex;
            for (bInputLineIndex = 0; bInputLineIndex < b.InputLines.Length; bInputLineIndex++)
            {
                ref var bInputLine = ref b.InputLines[bInputLineIndex];
                inConnection = bInputLine.ConnectionIn;

                for (aOutLineIndex = 0; aOutLineIndex < a.OutputLines.Length; aOutLineIndex++)
                {
                    ref var outputLine = ref a.OutputLines[aOutLineIndex]; // Avoid copying data from array
                    if (bInputLine.Type != outputLine.Output.ValueType)
                        continue;

                    // vertical
                    if (aOutLineIndex == 0 && bInputLineIndex == 0)
                    {
                        TestAndKeepPositionsForSnapping(ref outputLine,
                                                        0,
                                                        MagGraphItem.Directions.Vertical,
                                                        new Vector2(a.Area.Min.X + MagGraphItem.WidthHalf, a.Area.Max.Y),
                                                        new Vector2(b.Area.Min.X + MagGraphItem.WidthHalf, b.Area.Min.Y));
                    }

                    // horizontal
                    if (outputLine.Output.ValueType == bInputLine.Input.ValueType)
                    {
                        TestAndKeepPositionsForSnapping(ref outputLine,
                                                        bInputLine.MultiInputIndex,
                                                        MagGraphItem.Directions.Horizontal,
                                                        new Vector2(a.Area.Max.X, a.Area.Min.Y + (0.5f + outputLine.VisibleIndex) * MagGraphItem.LineHeight),
                                                        new Vector2(b.Area.Min.X, b.Area.Min.Y + (0.5f + bInputLine.VisibleIndex) * MagGraphItem.LineHeight));
                    }
                }
            }

            //Direction = MagGraphItem.Directions.Horizontal;
            return;

            void TestAndKeepPositionsForSnapping(ref MagGraphItem.OutputLine outputLine,
                                                 int multiInputIndexIfValid,
                                                 MagGraphItem.Directions directionIfValid,
                                                 Vector2 outPos,
                                                 Vector2 inPos)
            {
                // If input is connected the only valid output is the one with the connection line
                if (inConnection != null && outputLine.ConnectionsOut.All(c => c != inConnection))
                    return;

                var d = Vector2.Distance(outPos, inPos);
                if (d >= BestDistance)
                    return;

                BestDistance = d;
                OutAnchorPos = outPos;
                InputAnchorPos = inPos;
                OutLineIndex = aOutLineIndex;
                InputLineIndex = bInputLineIndex;
                BestA = a;
                BestB = b;
                Reverse = revert;
                Direction = directionIfValid;
                IsInsertion = false;
                MultiInputIndex = multiInputIndexIfValid;
            }

            // void ShowDebugLine(Vector2 outPos, Vector2 inPos, Type connectionType)
            // {
            //     if (!canvas.ShowDebug)
            //         return;
            //
            //     var drawList = ImGui.GetForegroundDrawList();
            //     var uiPrimaryColor = TypeUiRegistry.GetPropertiesForType(connectionType).Color;
            //     drawList.AddLine(canvas.TransformPosition(outPos),
            //                      canvas.TransformPosition(inPos),
            //                      uiPrimaryColor.Fade(0.4f));
            //
            //     drawList.AddCircleFilled(canvas.TransformPosition(inPos), 6, uiPrimaryColor.Fade(0.4f));
            // }
        }

        public void TestItemsForInsertion(MagGraphItem item, MagGraphItem insertionAnchorItem, SplitInsertionPoint insertionPoint)
        {
            if (item.InputLines.Length < 1)
                return;

            var mainInput = item.InputLines[0];

            // Vertical
            if (mainInput.ConnectionIn == null || mainInput.Type != insertionPoint.Type)
                return;

            if (mainInput.ConnectionIn.Style == MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical && insertionPoint.Direction == MagGraphItem.Directions.Vertical)
            {
                var inputPos = item.PosOnCanvas + new Vector2(MagGraphItem.GridSize.X / 2, 0);
                var insertionAnchorPos = insertionAnchorItem.PosOnCanvas + new Vector2(MagGraphItem.GridSize.X / 2, 0);
                var d = Vector2.Distance(insertionAnchorPos, inputPos);
                
                if (d >= BestDistance)
                    return;
                
                BestDistance = d;
                OutAnchorPos = inputPos;
                InputAnchorPos = insertionAnchorPos;
                OutLineIndex = 0;
                InputLineIndex = 0;
                BestA = item;
                BestB = null;
                Reverse = false;
                Direction = MagGraphItem.Directions.Vertical;
                MultiInputIndex = 0;
                IsInsertion = true;
                InsertionPoint = insertionPoint;
            }
            else if (mainInput.ConnectionIn.Style == MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedHorizontal && insertionPoint.Direction == MagGraphItem.Directions.Horizontal)
            {
                var inputAnchorOffset = new Vector2( 0, MagGraphItem.GridSize.Y / 2);
                var inputPos = item.PosOnCanvas + inputAnchorOffset;
                var insertionAnchorPos = insertionAnchorItem.PosOnCanvas + inputAnchorOffset;
                var d = Vector2.Distance(insertionAnchorPos, inputPos);

                if (d >= BestDistance)
                    return;

                BestDistance = d;
                OutAnchorPos = inputPos;
                InputAnchorPos = insertionAnchorPos;
                OutLineIndex = 0;
                InputLineIndex = 0;
                BestA = item;
                BestB = null;
                Reverse = false;
                Direction = MagGraphItem.Directions.Horizontal;
                MultiInputIndex = 0;
                IsInsertion = true;
                InsertionPoint = insertionPoint;
            }
        }

        public void Reset()
        {
            BestDistance = float.PositiveInfinity;
        }
    }
}