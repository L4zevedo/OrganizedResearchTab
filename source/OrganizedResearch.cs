using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;  // Stopwatch
using System.Reflection;

using UnityEngine;         // Always needed
//using VerseBase;         // Material/Graphics handling functions are found here
using Verse;               // RimWorld universal objects are here (like 'Building')
//using Verse.AI;          // Needed when you do something with the AI
//using Verse.Sound;       // Needed when you do something with Sound
//using Verse.Noise;       // Needed when you do something with Noises
using RimWorld;            // RimWorld specific functions are found here (like 'Building_Battery')
//using RimWorld.Planet;   // RimWorld specific functions for world creation
//using RimWorld.SquadAI;  // RimWorld specific functions for squad brains

namespace OrganizedResearch
{
    public class OrganizedResearch : MainTabWindow_Research
    {
        protected const int maxWidth = 9;
        protected const int maxOriginalWidth = 6;

        protected const float yStep = 0.65f;
        protected const float xStep = 1.00f;

        private IEnumerable<ResearchProjectDef> relevantProjects
        {
            get
            {
                var property = typeof(MainTabWindow_Research)
                             .GetField("relevantProjects", BindingFlags.Instance | BindingFlags.NonPublic);
                return (IEnumerable<ResearchProjectDef>)property.GetValue(this);
            }
        }

        /// <summary>
        /// Custom class constructor.
        /// </summary>
        public OrganizedResearch()
        {
            Stopwatch sw = new Stopwatch();

            try
            {
                sw.Start();

                List<ResearchProjectDef> topologicalOrder = DefDatabase<ResearchProjectDef>.AllDefsListForReading.ListFullCopy();
                organizeResearchTab(topologicalOrder);
                
                sw.Stop();
                Log.Message(sw.ElapsedMilliseconds + "ms organizing Research Tab.");
            }
            catch (Exception e)
            {
                Log.Error("OrganizedResearch: unidentified error.");
                Log.Notify_Exception(e);
            }
            finally
            {
                ResearchProjectDef.GenerateNonOverlappingCoordinates();
            }
        }

        /// <summary>
        /// Run the whole Sugiyama framework style drawing.
        /// </summary>
        /// <param name="topologicalOrder"></param>
        protected void organizeResearchTab(List<ResearchProjectDef> topologicalOrder)
        {
            // step 1 - enforce topological order - O(n^2)ish
            // also, populate requiredByThis to make layering easier
            EnforceTopologicalOrdering(topologicalOrder);

            // step 2 - transitive reduction (TODO if necessary)

            // step 3 - find a topological order compliant with Coffman–Graham algorithm - O(n^2)
            List<ResearchProjectDef> goodTopologicalOrder = CoffmanGrahamOrdering(topologicalOrder);
            
            //PrintOrder(goodTopologicalOrder);

            // step 4 - distribute tasks among hierarchical layers - O(n)?
            int currentLayer = 0; // x axis
            List<List<ResearchProjectDef>> Layers = new List<List<ResearchProjectDef>>();
            Layers.Add(new List<ResearchProjectDef>(maxWidth));

            while (goodTopologicalOrder.Count > 0)
            {
                ResearchProjectDef last = goodTopologicalOrder.Last();

                bool sharedLayer = false;
                foreach (ResearchProjectDef child in last.requiredByThis ?? Enumerable.Empty<ResearchProjectDef>())
                {
                    // we don't want a parent in the same layer as its children
                    if (Layers[currentLayer].Contains(child))
                    {
                        sharedLayer = true;
                    }
                }

                if (Layers[currentLayer].Count >= maxOriginalWidth || sharedLayer)
                {
                    currentLayer++;
                    Layers.Add(new List<ResearchProjectDef>(maxWidth));
                }

                Layers[currentLayer].Add(last);
                goodTopologicalOrder.RemoveLast();
            }
            
            // we did layering backwards, let's reverse all
            foreach (List<ResearchProjectDef> Layer in Layers)
            {
                Layer.Reverse();
            }
            Layers.Reverse();

            // step 4.1 - a very specific heuristic regarding lonely project (no dependencies)
            // since Coffman-Graham tends to generate layerings with thick bases and empty tops
            // we promote projects with no prereqs or "postreqs" one layer up
            for (int j = 1; j < Layers.Count; j++)
            {
                for (int i = 0; i < Layers[j].Count; i++)
                {
                    if (Layers[j][i].prerequisites == null && Layers[j][i].requiredByThis == null && Layers[j-1].Count < maxWidth)
                    {
                        Layers[j-1].Add(Layers[j][i]);
                        Layers[j].Remove(Layers[j][i]);
                        i--;
                    }
                }
            }

            // step 5 - add dummy nodes to layers, to make edges more visible
            for (int i = 0; i < Layers.Count - 1; i++) // for all existing layers except the last one
            {
                foreach (ResearchProjectDef current in Layers[i]) // for all projects in a layer
                {
                    ResearchProjectDef currentDummy = null;
                    
                    for (int k = 0; k < (current.requiredByThis?.Count ?? 0); k++ ) // for all "postreqs" of a project
                    {
                        for (int j = i + 2; j < Layers.Count; j++) // check if they are in layers ahead (long edge)
                        {
                            if (Layers[j].Contains(current.requiredByThis[k]) && (Layers[i + 1].Count < maxWidth || currentDummy != null))
                            { // found the layer with this "postreq" and there is room for a dummy or there already is a dummy
                                if (currentDummy == null)
                                {
                                    ResearchProjectDef dummy = new ResearchProjectDef();
                                    dummy.requiredByThis = new List<ResearchProjectDef>();
                                    dummy.defName = "d" + current.defName;
                                    Layers[i + 1].Insert(0, dummy);
                                    currentDummy = dummy;
                                }
                                currentDummy.requiredByThis.Add(current.requiredByThis[k]);
                                current.requiredByThis.Add(currentDummy); 

                                //break; // we found the layer where the "postreq" is, stop looking
                            }
                        }
                    }
                }
            }

            // step 6 - minimize edge crossings with vertex ordering within layers
            Layers = VertexOrderingWithinLayers(Layers);

            // step 7 - set X and Y coordinates based off layering


            float x = 0f, y;
            for (int i = 0; i < Layers.Count; i++)
            {
                y = 0f;
                foreach (ResearchProjectDef current in Layers[i])
                {
                    current.researchViewX = x;
                    current.researchViewY = y + (yStep / 2.0f) * (float)(i % 2);
                    y += yStep;
                }
                x += xStep;
            }
            
        }

        /// <summary>
        /// STEP 1 - Enforce basic topological ordering of a list and populate requiredByThis of each vertex for easier navigation.
        /// </summary>
        /// <param name="topologicalOrder"></param>
        protected void EnforceTopologicalOrdering(List<ResearchProjectDef> topologicalOrder)
        {
            foreach (ResearchProjectDef current in topologicalOrder)
            {
                foreach (ResearchProjectDef dependency in current.prerequisites ?? Enumerable.Empty<ResearchProjectDef>())
                {
                    int index = topologicalOrder.IndexOf(dependency);
                    int index2 = topologicalOrder.IndexOf(current);
                    if (index > index2)
                    {
                        SwapInList(topologicalOrder, index, index2);
                    }

                    if (dependency.requiredByThis == null)
                    {
                        dependency.requiredByThis = new List<ResearchProjectDef>();
                    }
                    dependency.requiredByThis.Add(current);
                }
            }
        }

        /// <summary>
        /// STEP 3 - Generate a topological ordering according to Coffman-Graham algorithm.
        /// </summary>
        /// <param name="topologicalOrder"></param>
        /// <returns></returns>
        protected List<ResearchProjectDef> CoffmanGrahamOrdering(List<ResearchProjectDef> topologicalOrder)
        {
            List<ResearchProjectDef> goodTopologicalOrder = new List<ResearchProjectDef>(topologicalOrder.Count);

            while (topologicalOrder.Count > 0)
            {
                ResearchProjectDef selected = topologicalOrder.First();

                foreach (ResearchProjectDef current in topologicalOrder)
                {
                    if (selected == current) continue;

                    if (current.prerequisites == null)
                    {
                        if (selected.prerequisites == null)
                        {
                            if (current.techLevel < selected.techLevel)
                            {
                                selected = current;
                            }
                        }
                        else
                        {
                            selected = current;
                        }
                    }
                    else
                    {
                        if (selected.prerequisites == null) break;

                        bool eligible = true;
                        foreach (ResearchProjectDef currentPR in current.prerequisites)
                        {
                            if (!goodTopologicalOrder.Contains(currentPR))
                            {
                                eligible = false;
                            }
                        }

                        if (selected.prerequisites != null && eligible)
                        {
                            List<int> selectedPRMax = new List<int>();
                            List<int> currentPRMax = new List<int>();
                            foreach (ResearchProjectDef selectedPR in selected.prerequisites)
                            {
                                selectedPRMax.Add(goodTopologicalOrder.IndexOf(selectedPR));
                            }
                            selectedPRMax.Sort(); selectedPRMax.Reverse();
                            foreach (ResearchProjectDef currentPR in current.prerequisites)
                            {
                                currentPRMax.Add(goodTopologicalOrder.IndexOf(currentPR));
                            }
                            currentPRMax.Sort(); currentPRMax.Reverse();

                            int i;
                            for (i = 0; i < selectedPRMax.Count && i < currentPRMax.Count; i++)
                            {
                                if (selectedPRMax[i] > currentPRMax[i])
                                {
                                    selected = current;
                                    break;
                                }
                                else if (selectedPRMax[i] < currentPRMax[i])
                                {
                                    break;
                                }
                            }
                            if (i < selectedPRMax.Count && i == currentPRMax.Count)
                            {
                                selected = current;
                            }
                        }
                    }
                }
                goodTopologicalOrder.Add(selected);
                topologicalOrder.Remove(selected);
            }

            return goodTopologicalOrder;
        }


        /// <summary>
        /// STEP 6
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        protected List<List<ResearchProjectDef>> VertexOrderingWithinLayers(List<List<ResearchProjectDef>> order)
        {
            List<List<ResearchProjectDef>> best = SaveOrder(order);
            for (int i = 0; i < 50; i++)
            {
                WeightedMedian(order, i);
                Transpose(order);
                if (CountTotalCrossings(order) < CountTotalCrossings(best))
                {
                    best = SaveOrder(order);
                }
            }

            return best;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        protected List<List<ResearchProjectDef>> SaveOrder(List<List<ResearchProjectDef>> order)
        {
            List<List<ResearchProjectDef>> Order = order.ListFullCopy();
            for (int i = 0; i < Order.Count; i++)
            {
                Order[i] = order[i].ListFullCopy();
            }

            return Order;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Order"></param>
        /// <param name="iteration"></param>
        protected void WeightedMedian(List<List<ResearchProjectDef>> Order, int iteration)
        {
            if (iteration % 2 == 0)
            {
                for (int i = 1; i < Order.Count; i++)
                {
                    float[] median = new float[Order[i].Count];
                    for (int j = 0; j < Order[i].Count; j++)
                    {
                        median[j] = MedianValue(Order[i][j], Order[i - 1], true);
                    }
                    SortLayer(Order[i], new List<float>(median));
                }
            }
            else
            {
                for (int i = Order.Count - 2; i >= 0; i--)
                {
                    float[] median = new float[Order[i].Count];
                    for (int j = 0; j < Order[i].Count; j++)
                    {
                        median[j] = MedianValue(Order[i][j], Order[i + 1], false);
                    }
                    SortLayer(Order[i], new List<float>(median));
                }
            }
        }

        /// <summary>
        /// Calculate a median value for a vertex in relation to the layer to the left or right.
        /// </summary>
        /// <param name="vertex"></param>
        /// <param name="adjacentLayer"></param>
        /// <param name="leftToRight"></param>
        /// <returns></returns>
        protected float MedianValue(ResearchProjectDef vertex, List<ResearchProjectDef> adjacentLayer, bool toTheLeft)
        {
            int[] P;

            if (toTheLeft)
            {
                P = AdjacentPositionsToTheLeft(vertex, adjacentLayer);
            }
            else
            {
                P = AdjacentPositionsToTheRight(vertex, adjacentLayer); 
            }
                            
            int numP = P.Count();
            int m = numP / 2;
            if (numP == 0)
            {
                return -1f;
            }
            else if (numP % 2 == 1)
            {
                return P[m];
            }
            else if (numP == 2)
            {
                return (P[0] + P[1]) / 2f;
            }
            else
            {
                float left = P[m - 1] - P[0];
                float right = P[numP - 1] - P[m];
                return (P[m - 1] * right + P[m] * left) / (left + right);
            }
        }

        /// <summary>
        /// Generate a array of positions of neighbors of a vertex in the layer to the left.
        /// </summary>
        /// <param name="vertex"></param>
        /// <param name="adjacentLayer"></param>
        /// <returns></returns>
        protected int[] AdjacentPositionsToTheLeft(ResearchProjectDef vertex, List<ResearchProjectDef> adjacentLayer)
        {
            List<int> positions = new List<int>();
            for (int i = 0; i < adjacentLayer.Count; i++)
            {
                for (int j = 0; j < (adjacentLayer[i].requiredByThis?.Count ?? 0); j++)
                {
                    if (adjacentLayer[i].requiredByThis[j] == vertex)
                    {
                        positions.Add(i);
                        break;
                    }
                }
            }

            return positions.ToArray();
        }

        /// <summary>
        /// Generate a array of positions of neighbors of a vertex in the layer to the right.
        /// </summary>
        /// <param name="vertex"></param>
        /// <param name="adjacentLayer"></param>
        /// <returns></returns>
        protected int[] AdjacentPositionsToTheRight(ResearchProjectDef vertex, List<ResearchProjectDef> adjacentLayer)
        {
            List<int> positions = new List<int>();
            for (int i = 0; i < adjacentLayer.Count; i++)
            {
                for (int j = 0; j < (vertex.requiredByThis?.Count ?? 0); j++)
                {
                    if (vertex.requiredByThis[j] == adjacentLayer[i])
                    {
                        positions.Add(i);
                        break;
                    }
                }
            }

            return positions.ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Order"></param>
        /// <param name="median"></param>
        protected void SortLayer(List<ResearchProjectDef> Order, List<float> median)
        {
            for (int i = 0; i < Order.Count; i++)
            {
                int min = i;
                if (median[i] == -1f)
                { // a value of -1f indicates a fixed vertex
                    continue;
                }
                for (int j = i + 1; j < Order.Count; j++)
                {
                    if (median[j] >= 0f && median[j] < median[min])
                    {
                        min = j;
                    }
                }
                float medianMove = median[min];
                median.RemoveAt(min);
                median.Insert(i, medianMove);
                ResearchProjectDef vertexMove = Order[min];
                Order.RemoveAt(min);
                Order.Insert(i, vertexMove);
            }
        }

        /// <summary>
        /// The transposition heuristic for reducing edge crossings.
        /// </summary>
        /// <param name="Order"></param>
        protected void Transpose(List<List<ResearchProjectDef>> Order)
        {
            bool improved = true;

            while (improved)
            {
                improved = false;
                for (int r = 1; r < Order.Count; r++)
                {
                    for (int i = 0; i < Order[r].Count - 1; i++)
                    {
                        int crossings = CountCrossingsBetweenLayers(Order[r - 1], Order[r]);
                        SwapInList(Order[r], i, i + 1);
                        if (crossings > CountCrossingsBetweenLayers(Order[r - 1], Order[r]))
                        {
                            improved = true;
                        }
                        else
                        {
                            SwapInList(Order[r], i, i + 1);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Order"></param>
        /// <returns></returns>
        protected int CountTotalCrossings(List<List<ResearchProjectDef>> Order)
        {
            int sum = 0;
            for (int i = 0; i < Order.Count - 1; i++)
            {
                sum += CountCrossingsBetweenLayers(Order[i], Order[i + 1]);
            }
            return sum;
        }

        /// <summary>
        /// Counts crossing between tight edges on adjacent layers according to the algorithm described in the paper:
        /// "Counting edge crossings in a 2-layered drawing" by Hiroshi Nagamochi
        /// </summary>
        /// <param name="layerA">Fixed layer</param>
        /// <param name="layerB">Permuted layer</param>
        /// <returns></returns>
        protected int CountCrossingsBetweenLayers(List<ResearchProjectDef> layerA, List<ResearchProjectDef> layerB)
        {
            int sum = 0;

            layerB.Reverse();
            for (int i = 1; i < layerA.Count; i++)
            {
                for (int j = 1; j < layerB.Count; j++)
                {
                    if (layerA[i].requiredByThis?.Contains(layerB[j]) ?? false)
                    {
                        sum += CountEdgesInRange(layerA, layerB, i - 1, j - 1);
                    }
                }
            }
            layerB.Reverse();

            return sum;
        }

        /// <summary>
        /// Count the amount of edges in a range of vertices pertinent of two adjacent layers.
        /// </summary>
        /// <param name="layerA"></param>
        /// <param name="layerB"></param>
        /// <param name="layerAindex"></param>
        /// <param name="layerBindex"></param>
        /// <returns></returns>
        protected int CountEdgesInRange(List<ResearchProjectDef> layerA, List<ResearchProjectDef> layerB, int layerAindex, int layerBindex)
        {
            int sum = 0;

            if (layerAindex < 0 || layerBindex < 0)
            {
                return 0;
            }

            sum += CountEdgesInRange(layerA, layerB, layerAindex, layerBindex - 1);
            sum += CountEdgesInRange(layerA, layerB, layerAindex - 1, layerBindex);
            sum -= CountEdgesInRange(layerA, layerB, layerAindex - 1, layerBindex - 1);

            if (layerA[layerAindex].requiredByThis?.Contains(layerB[layerBindex]) ?? false)
            {
                sum += 1;
            }

            return sum;
        }

        /// <summary>
        /// Simple swap of vertices in a list.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <param name="indexA"></param>
        /// <param name="indexB"></param>
        private void SwapInList<T>(List<T> list, int indexA, int indexB)
        {
            T tmp = list[indexA];
            list[indexA] = list[indexB];
            list[indexB] = tmp;
        }

        private void PrintList(List<ResearchProjectDef> list)
        {
            foreach (ResearchProjectDef current in list)
            {
                Log.Message(current.defName);
            }
        }

        /// <summary>
        /// Debug method to print the topological ordering of a list of vertices.
        /// </summary>
        /// <param name="list"></param>
        private void PrintTopologicalOrdering(List<ResearchProjectDef> list)
        {
            foreach (ResearchProjectDef current in list)
            {
                Log.Message(current.defName);
                foreach (ResearchProjectDef dependency in (current.prerequisites ?? Enumerable.Empty<ResearchProjectDef>()))
                {
                    Log.Message("   |- " + dependency.defName);
                }
                Log.Message("");
            }
        }

        /// <summary>
        /// Debug method to print the state of tight edges between adjacent layers.
        /// </summary>
        /// <param name="Layer"></param>
        /// <param name="nextLayer"></param>
        /// <param name="index"></param>
        private void PrintLayerAndTightEdges(List<ResearchProjectDef> Layer, List<ResearchProjectDef> nextLayer, int index)
        {
            Log.Message("Layer " + index);
            for (int i = 0; i < Layer.Count - 1; i++)
            {
                Log.Message(Layer[i].defName);
                foreach (ResearchProjectDef postreq in (Layer[i].requiredByThis ?? Enumerable.Empty<ResearchProjectDef>()))
                {
                    if (nextLayer.Contains(postreq))
                    {
                        Log.Message("   |- " + postreq.defName);
                    }
                }
                Log.Message("");
            }
        }
    }
}
