using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;  // Stopwatch

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

        // holds the layering throughout execution and between instances
        private static List<List<ResearchProjectDef>> _Layers = null;

        /******************************************************************************************
         * 
         * Default constructor
         * 
         * 
         ******************************************************************************************/
        public OrganizedResearch()
        {
            Stopwatch sw = new Stopwatch();

            // if we already calculated the layering, no need to do it again
            // this prevents the game from doing all calculations again when switching colonies/camps
            if (_Layers != null)
            {
                return;
            }

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


        /******************************************************************************************
         * 
         * Runs the whole Sugiyama framework style drawing.
         * 
         * Based on paper:
         * "Methods for Visual Understanding of Hierarchical System Structures"
         * by Kozo Sugiyama
         * 
         ******************************************************************************************/
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
            _Layers = new List<List<ResearchProjectDef>>();
            _Layers.Add(new List<ResearchProjectDef>(maxWidth));

            while (goodTopologicalOrder.Count > 0)
            {
                ResearchProjectDef last = goodTopologicalOrder.Last();

                bool sharedLayer = false;
                foreach (ResearchProjectDef child in last.requiredByThis ?? Enumerable.Empty<ResearchProjectDef>())
                {
                    // we don't want a parent in the same layer as its children
                    if (_Layers[currentLayer].Contains(child))
                    {
                        sharedLayer = true;
                    }
                }

                if (_Layers[currentLayer].Count >= maxOriginalWidth || sharedLayer)
                {
                    currentLayer++;
                    _Layers.Add(new List<ResearchProjectDef>(maxWidth));
                }

                _Layers[currentLayer].Add(last);
                goodTopologicalOrder.RemoveLast();
            }
            
            // we did layering backwards, let's reverse all
            foreach (List<ResearchProjectDef> Layer in _Layers)
            {
                Layer.Reverse();
            }
            _Layers.Reverse();

            // step 4.1 - a very specific heuristic regarding lonely project (no dependencies)
            // since Coffman-Graham tends to generate layerings with thick bases and empty tops
            // we promote projects with no prereqs or "postreqs" one layer up
            for (int j = 1; j < _Layers.Count; j++)
            {
                for (int i = 0; i < _Layers[j].Count; i++)
                {
                    if (_Layers[j][i].prerequisites == null && _Layers[j][i].requiredByThis == null && _Layers[j-1].Count < maxWidth)
                    {
                        _Layers[j-1].Add(_Layers[j][i]);
                        _Layers[j].Remove(_Layers[j][i]);
                        i--;
                    }
                }
            }

            // step 5 - add dummy nodes to layers, to make edges more visible
            for (int i = 0; i < _Layers.Count - 1; i++) // for all existing layers except the last one
            {
                foreach (ResearchProjectDef current in _Layers[i]) // for all projects in a layer
                {
                    ResearchProjectDef currentDummy = null;
                    
                    for (int k = 0; k < (current.requiredByThis?.Count ?? 0); k++ ) // for all "postreqs" of a project
                    {
                        for (int j = i + 2; j < _Layers.Count; j++) // check if they are in layers ahead (long edge)
                        {
                            if (_Layers[j].Contains(current.requiredByThis[k]) && (_Layers[i + 1].Count < maxWidth || currentDummy != null))
                            { // found the layer with this "postreq" and there is room for a dummy or there already is a dummy
                                if (currentDummy == null)
                                {
                                    ResearchProjectDef dummy = new ResearchProjectDef();
                                    dummy.requiredByThis = new List<ResearchProjectDef>();
                                    dummy.defName = "d" + current.defName;
                                    _Layers[i + 1].Insert(0, dummy);
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
            _Layers = VertexOrderingWithinLayers(_Layers);

            // step 7 - set X and Y coordinates based off layering
            // trivial assignment
            // TODO better assignment based off:
            // "Fast and Simple Horizontal Coordinate Assignment"
            // by Ulrik Brandes and Boris Köpf
            float x = 0f, y;
            for (int i = 0; i < _Layers.Count; i++)
            {
                y = 0f;
                foreach (ResearchProjectDef current in _Layers[i])
                {
                    current.researchViewX = x;
                    current.researchViewY = y + (yStep / 2.0f) * (float)(i % 2);
                    y += yStep;
                }
                x += xStep;
            }
            
        }

        /******************************************************************************************
         * 
         * STEP 1
         * 
         * Enforce basic topological ordering of a list and populate requiredByThis of each vertex
         * for easier navigation.
         * 
         * 
         ******************************************************************************************/
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

        /******************************************************************************************
         * 
         * STEP 3
         * 
         * Generate a topological ordering according to Coffman-Graham algorithm.
         * 
         * Based on:
         * "Hierarchical Drawing Algorithms"
         * by Patrick Healy and Nikola S. Nikolov
         * 
         * https://cs.brown.edu/~rt/gdhandbook/chapters/hierarchical.pdf
         * 
         ******************************************************************************************/
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



        /******************************************************************************************
         * 
         * STEP 6
         * 
         * Reorders vertices withing layers according to two heuristics:
         *     - Median value of neighboring layers
         *     - Permutations
         *     
         * Based on the paper:
         * "A Technique for Drawing Directed Graphs"
         * by Emden R. Gansner et al.
         * 
         ******************************************************************************************/
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


        /******************************************************************************************
         * 
         * Saves the current state of the graph.
         * 
         * 
         ******************************************************************************************/
        protected List<List<ResearchProjectDef>> SaveOrder(List<List<ResearchProjectDef>> order)
        {
            List<List<ResearchProjectDef>> Order = order.ListFullCopy();
            for (int i = 0; i < Order.Count; i++)
            {
                Order[i] = order[i].ListFullCopy();
            }

            return Order;
        }


        /******************************************************************************************
         * 
         * Sweeps the graph, layer by layer, from left to right or right to left depending on the
         * iteration.
         * 
         * 
         ******************************************************************************************/
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


        /******************************************************************************************
         * 
         * Calculate a median value for a vertex in relation to the layer to the left or right.
         * 
         * 
         ******************************************************************************************/
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


        /******************************************************************************************
         * 
         * Generate an array of positions of neighbors of a vertex in the layer to the left.
         * 
         * 
         ******************************************************************************************/
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


        /******************************************************************************************
         * 
         * Generate an array of positions of neighbors of a vertex in the layer to the right.
         * 
         * 
         ******************************************************************************************/
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


        /******************************************************************************************
         * 
         * Selection sorts a layers based off some previously calculated median.
         * 
         * 
         ******************************************************************************************/
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


        /******************************************************************************************
         * 
         * The transposition heuristic for reducing edge crossings.
         * 
         * 
         ******************************************************************************************/
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


        /******************************************************************************************
         * 
         * Counts all edge crossings in the whole graph.
         * 
         * 
         ******************************************************************************************/
        protected int CountTotalCrossings(List<List<ResearchProjectDef>> Order)
        {
            int sum = 0;
            for (int i = 0; i < Order.Count - 1; i++)
            {
                sum += CountCrossingsBetweenLayers(Order[i], Order[i + 1]);
            }
            return sum;
        }


        /******************************************************************************************
         * 
         * Counts crossing between tight edges on adjacent layers.
         * 
         * Based on the paper:
         * "Counting edge crossings in a 2-layered drawing"
         * by Hiroshi Nagamochi
         * 
         ******************************************************************************************/
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


        /******************************************************************************************
         * 
         * Count the amount of edges in a range of vertices pertinent of two adjacent layers.
         * 
         * 
         ******************************************************************************************/
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


        /******************************************************************************************
         * 
         * Simple swap of vertices in a list.
         * 
         * 
         ******************************************************************************************/
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


        /******************************************************************************************
         * 
         * Debug method to print the topological ordering of a list of vertices.
         * 
         * 
         ******************************************************************************************/
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


        /******************************************************************************************
         * 
         *  Debug method to print the state of tight edges between adjacent layers.
         * 
         * 
         ******************************************************************************************/
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
