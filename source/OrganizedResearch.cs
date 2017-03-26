/* Lucas Azevedo
 * v1.1
 * - Calculations are now done in a separate thread, so impact in loading time should now be minimal
 */

using System;
using System.Collections.Generic;
using System.Threading;
using System.Reflection;

using UnityEngine;
using RimWorld;            // RimWorld specific functions are found here
using Verse;               // RimWorld universal objects are here
using Verse.Sound;

using Harmony;

namespace OrganizedResearch
{
    public class OrganizedResearch : MainTabWindow_Research
    {
        #region Fields
        Thread _thread;

        bool noBenchWarned = true;

        Traverse thisTab;
        Traverse relevantProjectsField;
        List<ResearchProjectDef> _relevantProjects;
        Traverse selectedProjectField;
        ResearchProjectDef _selectedProject;

        MethodInfo DrawLeftRectInfo;

        const float LayerWidth   = 220f;
        const float LayerHeight  = 75f;
        const float VertexWidth  = 150f;
        const float VertexHeight = 50f;

        // holds the layering throughout execution and between instances
        static Layering _layering = null;

        static bool _coordsTransfer = false;

        static Vector2 _ScrollPosition = default(Vector2);

        //static bool debuggin = false;
        #endregion

        #region Constructors
        /******************************************************************************************
         * 
         * Default constructor
         * 
         * 
         ******************************************************************************************/
        public OrganizedResearch()
        {
            // helps deal with private methods/fields
            thisTab = Traverse.Create(this);
            relevantProjectsField = thisTab.Field("relevantProjects");
            selectedProjectField = thisTab.Field("selectedProject");
            DrawLeftRectInfo = typeof(MainTabWindow_Research).GetMethod("DrawLeftRect", BindingFlags.Instance | BindingFlags.NonPublic);

            // if we already calculated the layering, no need to do it again
            // this prevents the game from doing all calculations again when switching colonies/camps
            if (_layering == null)
            {
                _layering = new Layering(DefDatabase<ResearchProjectDef>.AllDefsListForReading);
                _thread = new Thread(new ThreadStart(_layering.OrganizeResearchTab));
                _thread.Start();
            }
        }
        #endregion

        #region Method Override
        /******************************************************************************************
         * 
         * PreOpen override
         * 
         * 
         ******************************************************************************************/
        public override void PreOpen()
        {
            base.PreOpen();

            // set to topleft (for some reason vanilla alignment overlaps bottom buttons)
            // from Fluffy's Research Tree
            windowRect.x = 0f;
            windowRect.y = 200f;
            windowRect.width = Screen.width;
            windowRect.height = Screen.height - 235f;

            if (!_coordsTransfer)
            {
                _thread.Join();
                _layering.TransferCoordinates(DefDatabase<ResearchProjectDef>.AllDefsListForReading);
                _coordsTransfer = true;
            }
        }

        /******************************************************************************************
         * 
         * DoWindowContents override
         * 
         * 
         ******************************************************************************************/
        public override void DoWindowContents(Rect inRect)
        {
            if (!noBenchWarned)
            {
                bool flag = false;
                List<Map> maps = Find.Maps;
                for (int i = 0; i < maps.Count; i++)
                {
                    if (maps[i].listerBuildings.ColonistsHaveResearchBench())
                    {
                        flag = true;
                        break;
                    }
                }
                if (!flag)
                {
                    Find.WindowStack.Add(new Dialog_MessageBox("ResearchMenuWithoutBench".Translate(), null, null, null, null, null, false));
                }
                noBenchWarned = true;
            }
            float num = 0f;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Rect leftOutRect = new Rect(0f, num, 200f, inRect.height - num);
            Rect rect = new Rect(leftOutRect.xMax + 10f, num, inRect.width - leftOutRect.width - 10f, inRect.height - num);
            Widgets.DrawMenuSection(rect, true);
            //thisTab.Method("DrawLeftRect").GetValue<void>(leftOutRect);
            DrawLeftRectInfo.Invoke(this, new object[] { leftOutRect });
            DrawRightRect(rect);
        }

        #endregion

        #region Own Methods
        /******************************************************************************************
         * 
         * DrawRightRect Prefix
         * 
         * Heavily edited copy/paste from decompiled original 
         * 
         * 
         ******************************************************************************************/
        void DrawRightRect(Rect rightOutRect)
        {
            _relevantProjects = relevantProjectsField.GetValue<List<ResearchProjectDef>>();
            _selectedProject = selectedProjectField.GetValue<ResearchProjectDef>();

            float viewWidth = _layering.maxX * LayerWidth;

            Rect outRect = rightOutRect.ContractedBy(10f);
            Rect rect = new Rect(0f, 0f, viewWidth, outRect.height - 16f);
            Rect position = rect.ContractedBy(10f);
            rect.width = viewWidth;
            position = rect.ContractedBy(10f);
            Widgets.ScrollHorizontal(outRect, ref _ScrollPosition, rect);
            Widgets.BeginScrollView(outRect, ref _ScrollPosition, rect);
            GUI.BeginGroup(position);

            //DEBUG
            //if (debuggin)
            //{
            //    Log.Message("relevant projects: " + _relevantProjects.Count);
            //    Log.Message("vertices: " + _layering.vertices.Count);
            //    debuggin = false;
            //}

            Vector2 start;
            Vector2 end;
            // draws the relationship lines
            foreach (Vertex v in _layering.vertices)
            {
                foreach (Vertex vParent in v.parents)
                {
                    start.x = v.x * LayerWidth;
                    start.y = v.y * LayerHeight + (VertexHeight / 2f);
                    end.x = vParent.x * LayerWidth + VertexWidth;
                    end.y = vParent.y * LayerHeight + VertexHeight / 2f;
                    Widgets.DrawLine(start, end, TexUI.DefaultLineResearchColor, 2f);
                    if (vParent.isDummy)
                    {
                        start.x = end.x - VertexWidth;
                        start.y = end.y;
                        Widgets.DrawLine(start, end, TexUI.DefaultLineResearchColor, 2f);
                    }
                }
            }

            // draw highlighted lines for selected project
            Vertex selected = _layering.MapProjectVertex[_selectedProject];
            DrawChildrenEdges(selected);
            DrawParentEdges(selected);

            //draws each project box
            foreach (Vertex v in _layering.vertices)
            {
                if (v.isDummy)
                {
                    continue;
                }
                Rect source = new Rect(v.x * LayerWidth, v.y * LayerHeight, VertexWidth, VertexHeight);
                string label = v.project.LabelCap + "\n(" + v.project.CostApparent.ToString("F0") + ")";
                Rect rect2 = new Rect(source);
                Color textColor = Widgets.NormalOptionColor;
                Color color = default(Color);
                Color borderColor = default(Color);
                bool flag = !v.project.IsFinished && !v.project.CanStartNow;
                if (v.project == Find.ResearchManager.currentProj)
                {
                    color = TexUI.ActiveResearchColor;
                }
                else if (v.project.IsFinished)
                {
                    color = TexUI.FinishedResearchColor;
                }
                else if (flag)
                {
                    color = TexUI.LockedResearchColor;
                }
                else if (v.project.CanStartNow)
                {
                    color = TexUI.AvailResearchColor;
                }
                if (_selectedProject == v.project)
                {
                    color += TexUI.HighlightBgResearchColor;
                    borderColor = TexUI.HighlightBorderResearchColor;
                }
                else
                {
                    borderColor = TexUI.DefaultBorderResearchColor;
                }

                if (flag)
                {
                    textColor = Color.gray;
                }
                foreach (Vertex vParent in v.parents)
                {
                    if (vParent != null && _selectedProject == vParent.project)
                    {
                        borderColor = TexUI.HighlightLineResearchColor;
                    }
                }
                foreach (Vertex vChild in v.children)
                {
                    if (_selectedProject == vChild.project)
                    {
                        borderColor = TexUI.HighlightLineResearchColor;
                    }
                }

                if (Widgets.CustomButtonText(ref rect2, label, color, textColor, borderColor, true, 1, true, true))
                {
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    selectedProjectField.SetValue(v.project);
                }
            }
            GUI.EndGroup();
            Widgets.EndScrollView();
        }


        /******************************************************************************************
         * 
         * Auxiliary drawing methods
         * 
         * 
         ******************************************************************************************/
        void DrawChildrenEdges(Vertex v)
        {
            Vector2 start;
            Vector2 end;
            foreach (Vertex child in v.children)
            {
                start.x = v.x * LayerWidth + VertexWidth;
                start.y = v.y * LayerHeight + (VertexHeight / 2f);
                end.x = child.x * LayerWidth;
                end.y = child.y * LayerHeight + VertexHeight / 2f;
                Widgets.DrawLine(start, end, TexUI.HighlightLineResearchColor, 4f);
                if (child.isDummy)
                {
                    start.x = end.x + VertexWidth;
                    start.y = end.y;
                    Widgets.DrawLine(start, end, TexUI.HighlightLineResearchColor, 4f);
                    DrawChildrenEdges(child);
                }
            }

        }

        void DrawParentEdges(Vertex v)
        {
            Vector2 start;
            Vector2 end;
           
            foreach (Vertex parent in v.parents)
            {
                start.x = v.x * LayerWidth;
                start.y = v.y * LayerHeight + (VertexHeight / 2f);
                end.x = parent.x * LayerWidth + VertexWidth;
                end.y = parent.y * LayerHeight + VertexHeight / 2f;
                Widgets.DrawLine(start, end, TexUI.HighlightLineResearchColor, 4f);
                if (parent.isDummy)
                {
                    start.x = end.x - VertexWidth;
                    start.y = end.y;
                    Widgets.DrawLine(start, end, TexUI.HighlightLineResearchColor, 4f);
                    DrawParentEdges(parent);
                }
            }
        }
        #endregion
    }
}
