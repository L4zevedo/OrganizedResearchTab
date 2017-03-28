/* Lucas Azevedo
 * 2017-03-25
 */

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;
using System.Linq;

using Verse;

namespace OrganizedResearch
{
    using Layer = List<Vertex>;

    internal class Layering
    {
        #region Fields
        int maxW; // by the Coffman-Graham definition of "width"

        // this was normalized to 1f, now changed via window drawing parameters
        const float yStep = 1f;
        const float xStep = 1f;

        // layering structure
        List<Layer> _layers;

        // list of projects
        public List<Vertex> vertices { get; private set; }
        #endregion

        #region Properties
        // mapping of Projects to Vertices
        public Dictionary<ResearchProjectDef, Vertex> MapProjectVertex { get; set; }

        // holds the number of iterations necessary for final result
        public int iterations { get; set; }

        public float maxX { get; private set; }

        public Stopwatch sw { get; } = new Stopwatch();
        #endregion


        #region Constructor
        /******************************************************************************************
         * 
         * Constructor
         * 
         * 
         ******************************************************************************************/
        public Layering(List<ResearchProjectDef> projects)
        {
            //maxW = maxLayerProjects;   592 - 9  / 953 
            maxW = 10;

            vertices = new List<Vertex>(projects.Count);
            MapProjectVertex = new Dictionary<ResearchProjectDef, Vertex>(projects.Count);
            
            foreach (ResearchProjectDef project in projects)
            {
                Vertex v = new Vertex(project, false);
                vertices.Add(v);
                MapProjectVertex.Add(project, v);
            }

            foreach (ResearchProjectDef project in projects)
            {
                foreach (ResearchProjectDef parent in (project.prerequisites ?? Enumerable.Empty<ResearchProjectDef>()))
                {
                    MapProjectVertex[project].parents.Add(MapProjectVertex[parent]);
                }
            }
        }
        #endregion

        #region Methods
        /******************************************************************************************
         * 
         * Runs the whole Sugiyama framework style drawing.
         * 
         * Based on paper:
         * "Methods for Visual Understanding of Hierarchical System Structures"
         * by Kozo Sugiyama
         * 
         ******************************************************************************************/
        public void OrganizeResearchTab()
        {
            sw.Start();
            // step 1 - populate requiredByThis to make layering easier
            PopulateChildren();

            // step 2 - transitive reduction (TODO if necessary)

            // step 3 - find a topological order compliant with Coffman–Graham algorithm
            CoffmanGrahamOrdering();

            // step 4 - distribute tasks among hierarchical layers
            DistributeTasksInLayers();

            // step 5 - add dummy nodes to layers, to make edges more visible
            AddDummyNodesToLayers();

            // step 6 - minimize edge crossings with vertex ordering within layers
            VertexOrderingWithinLayers();

            // step 6.1
            PushVerticesForward();
            
            // step 6.5
            RebuildVertexList();

            // step 7 - set X and Y coordinates based off layering
            AssignCoordinates();
            sw.Stop();
        }

        /******************************************************************************************
        * 
        * STEP 1
        * 
        * Populate requiredByThis of each vertex for easier navigation.
        * 
        * 
        ******************************************************************************************/
        void PopulateChildren()
        {
            for (int i = 0; i < vertices.Count; i++)
            {
                for (int j = 0; j < (vertices[i].parents.Count); j++)
                {
                    vertices[i].parents[j].children.Add(vertices[i]);
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
        void CoffmanGrahamOrdering()
        {
            bool selectedEligible;
            bool currentEligible;
            int selected;

            List<Vertex> newOrder = new List<Vertex>(vertices.Count);

            while (vertices.Count > 0)
            {
                selected = 0;

                for (int current = 1; current < vertices.Count; current++)
                {
                    if (vertices[current].parents.NullOrEmpty())
                    {
                        if (vertices[selected].parents.NullOrEmpty())
                        {
                            if (vertices[current].project.techLevel < vertices[selected].project.techLevel)
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
                        if (vertices[selected].parents.NullOrEmpty()) continue;

                        selectedEligible = true;
                        foreach (Vertex selectedPR in vertices[selected].parents)
                        {
                            if (!newOrder.Contains(selectedPR))
                            {
                                selectedEligible = false;
                                selected = current;
                            }
                        }

                        currentEligible = true;
                        foreach (Vertex currentPR in vertices[current].parents)
                        {
                            if (!newOrder.Contains(currentPR))
                            {
                                currentEligible = false;
                            }
                        }

                        if (selectedEligible && currentEligible)
                        {
                            List<int> selectedPRMax = new List<int>();
                            List<int> currentPRMax = new List<int>();
                            foreach (Vertex selectedPR in vertices[selected].parents)
                            {
                                selectedPRMax.Add(newOrder.IndexOf(selectedPR));
                            }
                            selectedPRMax.Sort(); selectedPRMax.Reverse();
                            foreach (Vertex currentPR in vertices[current].parents)
                            {
                                currentPRMax.Add(newOrder.IndexOf(currentPR));
                            }
                            currentPRMax.Sort(); currentPRMax.Reverse();

                            int i;
                            for (i = 0; (i < selectedPRMax.Count) && (i < currentPRMax.Count); i++)
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
                newOrder.Add(vertices[selected]);
                vertices.RemoveAt(selected);
            }

            vertices = newOrder;
        }


        /******************************************************************************************
         * 
         * STEP 4
         * 
         * Distribute tasks in layers
         * 
         * 
         ******************************************************************************************/
        void DistributeTasksInLayers()
        {
            int indexLayer = 0; // x axis
            _layers = new List<Layer>();
            _layers.Add(new Layer(maxW));

            while (vertices.Count > 0)
            {
                Vertex last = vertices.Last();

                bool sharedLayer = false;
                foreach (Vertex child in last.children)
                {
                    // we don't want a parent in the same layer as its children
                    if (_layers[indexLayer].Contains(child))
                    {
                        sharedLayer = true;
                    }
                }

                if (_layers[indexLayer].Count == maxW || sharedLayer)
                {
                    indexLayer++;
                    _layers.Add(new Layer(maxW));
                }

                _layers[indexLayer].Add(last);
                vertices.RemoveLast();
            }

            // we did layering backwards, let's reverse all
            foreach (Layer currentLayer in _layers)
            {
                currentLayer.Reverse();
            }
            _layers.Reverse();

            // step 4.1 - a very specific heuristic regarding lonely project (no dependencies)
            // since Coffman-Graham tends to generate layerings with thick bases and empty tops
            // we promote projects with no prereqs or "postreqs" one layer up
            for (int j = 1; j < _layers.Count; j++)
            { // for each layer
                for (int i = 0; i < _layers[j].Count; i++)
                { // for each project in the layer
                    if (_layers[j][i].parents.NullOrEmpty() && _layers[j][i].children.NullOrEmpty() && _layers[j - 1].Count < maxW)
                    {
                        _layers[j - 1].Add(_layers[j][i]);
                        _layers[j].RemoveAt(i);
                        i--;
                    }
                }
            }
        }


        /******************************************************************************************
         * 
         * STEP 5
         * 
         * Add dummy nodes, making every edge tight
         * 
         ******************************************************************************************/
        void AddDummyNodesToLayers()
        {
            for (int i = 0; i < _layers.Count - 1; i++) // for all existing layers except the last one
            {
                bool stillDummy = true;
                while (stillDummy)
                {
                    stillDummy = false;
                    foreach (Vertex v in _layers[i]) // for all projects in a layer
                    {
                        for (int k = 0; k < v.children.Count; k++) // for all "postreqs" of a project
                        {
                            Vertex child = v.children[k];
                            for (int j = i + 2; j < _layers.Count; j++) // check if they are in layers ahead (long edge)
                            {
                                if (_layers[j].Contains(child))
                                { // found the layer with this "postreq"
                                    stillDummy = true;
                                    if (v.dummy == null)
                                    {
                                        if (_layers[i + 1].Count == maxW)
                                        {
                                            Vertex demoted = DemoteVertex(i + 1); // open up space for a dummy
                                        }
                                        Vertex dummy = new Vertex(v.project, true);
                                        dummy.parents.Add(v);
                                        dummy.children.Add(child);
                                        v.children.Add(dummy);
                                        child.parents.Add(dummy);
                                        v.children.Remove(child);
                                        child.parents.Remove(v);
                                        _layers[i + 1].Add(dummy);
                                        v.dummy = dummy;
                                    }
                                    else
                                    {
                                        v.dummy.children.Add(child);
                                        child.parents.Add(v.dummy);
                                        v.children.Remove(child);
                                        child.parents.Remove(v);
                                    }

                                    break; // we found the layer where the "postreq" is,  looking
                                }
                            }
                        }
                    }
                }
            }
        }

        /******************************************************************************************
         * 
         * Demotes a vertex in this layer.
         * 
         * 
         ******************************************************************************************/
        Vertex DemoteVertex(int layerIndex)
        {
            if (layerIndex + 1 == _layers.Count)
            { // last layer, we need another one
                _layers.Add(new Layer());
            }

            Layer l1 = _layers[layerIndex];
            Layer l2 = _layers[layerIndex + 1];

            if (l2.Count == maxW)
            { // next layer is full, demote there
                DemoteVertex(layerIndex + 1);
            }

            int index;
            bool searching;
            for (index = l1.Count - 1; index > -1; index--)
            { // for each vertex in current layer
                searching = false;
                if (l1[index].isDummy)
                {
                    continue;
                }
                foreach (Vertex child in (l1[index].children))
                { // for each postreq in child
                    if (l2.Contains(child) )
                    {
                        searching = true;
                    }
                }
                if (!searching)
                {
                    break;
                }
            }

            if (index == -1)
            {
                Log.Error("Organized Research Tab: BIG TROUBLE.");
            }

            l2.Insert(0, l1[index]);
            l1.RemoveAt(index);

            return l2[0];
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
        void VertexOrderingWithinLayers()
        {
            List<Layer> best = SaveLayering();
            bool medianImprove;
            bool transpImprove;
            bool generalImprove;
            bool uselessRound = false;

            for (iterations = 0; (iterations < 20) && (!uselessRound); iterations++)
            {
                uselessRound = true;

                medianImprove = WeightedMedian();
                transpImprove = false;
                if (iterations > 3)
                { // using Transpose right from the beginning costs too much
                    transpImprove = Transpose();
                }

                generalImprove = false;
                if (CountTotalCrossings(_layers) < CountTotalCrossings(best))
                {
                    generalImprove = true;
                    best = SaveLayering();
                }

                if (medianImprove || transpImprove || generalImprove)
                {
                    uselessRound = false;
                }
            }

            _layers = best;
        }


        /******************************************************************************************
         * 
         * Saves the current state of the graph.
         * 
         * 
         ******************************************************************************************/
        List<Layer> SaveLayering()
        {
            List<Layer> saved = _layers.ListFullCopy();
            for (int i = 0; i < saved.Count; i++)
            {
                saved[i] = _layers[i].ListFullCopy();
            }

            return saved;
        }


        /******************************************************************************************
         * 
         * Sweeps the graph, layer by layer, from left to right or right to left depending on the
         * iteration.
         * 
         * Left to right sweeps tends to put a parent vertex close to its children
         * 
         * Right to left sweeps tends to put children close to each other
         * 
         ******************************************************************************************/
        bool WeightedMedian()
        {
            bool totalImprove = false;
            var median = new List<float>(maxW);

            if (iterations % 2 == 0)
            { // sweep layers from left to right
                for (int i = 1; i < _layers.Count; i++)
                {
                    median.Clear();
                    for (int j = 0; j < _layers[i].Count; j++)
                    {
                        median.Add(MedianValue(_layers[i][j], _layers[i - 1], true));
                    }

                    if (SortLayer(_layers[i], median))
                    {
                        totalImprove = true;
                    }
                }
            }
            else
            { // sweep layers from right to left
                for (int i = _layers.Count - 2; i > -1; i--)
                {
                    median.Clear();
                    for (int j = 0; j < _layers[i].Count; j++)
                    {
                        median.Add(MedianValue(_layers[i][j], _layers[i + 1], false));
                    }

                    if (SortLayer(_layers[i], median))
                    {
                        totalImprove = true;
                    }
                }
            }

            return totalImprove;
        }


        /******************************************************************************************
         * 
         * Calculate a median value for a vertex in relation to the layer to the left or right.
         * 
         * 
         ******************************************************************************************/
        float MedianValue(Vertex vertex, Layer adjacentLayer, bool toTheLeft)
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
                float up = P[m - 1] - P[0];
                float down = P[numP - 1] - P[m];
                return (P[m - 1] * down + P[m] * up) / (up + down);
            }
        }


        /******************************************************************************************
         * 
         * Generate an array of positions of neighbors of a vertex in the layer to the left.
         * 
         * 
         ******************************************************************************************/
        int[] AdjacentPositionsToTheLeft(Vertex vertex, Layer adjacentLayer)
        {
            var positions = new List<int>();
            for (int i = 0; i < adjacentLayer.Count; i++)
            {
                for (int j = 0; j < adjacentLayer[i].children.Count; j++)
                {
                    if (adjacentLayer[i].children[j] == vertex)
                    {
                        positions.Add(i);
                        break; // don't remove this break, or it could cause division by zero in MedianValue()
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
        int[] AdjacentPositionsToTheRight(Vertex vertex, Layer adjacentLayer)
        {
            List<int> positions = new List<int>();
            for (int i = 0; i < adjacentLayer.Count; i++)
            {
                for (int j = 0; j < (vertex.children.Count); j++)
                {
                    if (vertex.children[j] == adjacentLayer[i])
                    {
                        positions.Add(i);
                        break; // don't remove this break, or it could cause division by zero in MedianValue()
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
        bool SortLayer(Layer currentLayer, List<float> median)
        {
            bool improve = false;
            int min;

            for (int i = 0; i < currentLayer.Count - 1; i++)
            {
                min = i;
                if (median[i] < 0f)
                { // a negative value  indicates a fixed vertex
                    continue;
                }
                for (int j = i + 1; j < currentLayer.Count; j++)
                {
                    if (median[j] > 0f && median[j] < median[min])
                    {
                        min = j;
                    }
                }

                if (min > i)
                {
                    float medianMove = median[min];
                    median.RemoveAt(min);
                    median.Insert(i, medianMove);
                    Vertex vertexMove = currentLayer[min];
                    currentLayer.RemoveAt(min);
                    currentLayer.Insert(i, vertexMove);
                    improve = true;
                }
            }

            return improve;
        }


        /******************************************************************************************
         * 
         * The transposition heuristic for reducing edge crossings.
         * 
         * 
         ******************************************************************************************/
        bool Transpose()
        {
            bool localImprove = true;
            bool totalImprove = false;

            while (localImprove)
            {
                localImprove = false;
                for (int r = 1; r < _layers.Count; r++)
                {
                    for (int i = 0; i < _layers[r].Count - 1; i++)
                    {
                        int crossings = CountCrossingsBetweenLayers(_layers[r - 1], _layers[r]);
                        SwapInList(_layers[r], i, i + 1);
                        if (crossings > CountCrossingsBetweenLayers(_layers[r - 1], _layers[r]))
                        {
                            localImprove = true;
                            totalImprove = true;
                        }
                        else
                        {
                            SwapInList(_layers[r], i, i + 1);
                        }
                    }
                }
            }

            return totalImprove;
        }


        /******************************************************************************************
         * 
         * Counts all edge crossings in the whole graph.
         * 
         * 
         ******************************************************************************************/
        int CountTotalCrossings(List<Layer> layering)
        {
            int sum = 0;
            for (int i = 0; i < layering.Count - 1; i++)
            {
                sum += CountCrossingsBetweenLayers(layering[i], layering[i + 1]);
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
        int CountCrossingsBetweenLayers(Layer layerA, Layer layerB)
        {
            int sum = 0;

            layerB.Reverse();
            for (int i = 1; i < layerA.Count; i++)
            {
                for (int j = 1; j < layerB.Count; j++)
                {
                    if (layerA[i].children.Contains(layerB[j]))
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
        int CountEdgesInRange(Layer layerA, Layer layerB, int layerAindex, int layerBindex)
        {
            int sum = 0;

            if (layerAindex < 0 || layerBindex < 0)
            {
                return 0;
            }

            sum += CountEdgesInRange(layerA, layerB, layerAindex, layerBindex - 1);
            sum += CountEdgesInRange(layerA, layerB, layerAindex - 1, layerBindex);
            sum -= CountEdgesInRange(layerA, layerB, layerAindex - 1, layerBindex - 1);

            if (layerA[layerAindex].children.Contains(layerB[layerBindex]))
            {
                sum += 1;
            }

            return sum;
        }

        /******************************************************************************************
         *  STEP 6.1
         *  
         *  Try to push vertices forward if all they got a child is a dummy
         *  
         *  Not working. Ruins everything, to be reviewed.
         * 
         ******************************************************************************************/
        void PushVerticesForward()
        {
            for (int i = 0; i < _layers.Count - 1; i++)
            {
                for (int j = 0; j < _layers[i].Count; j++)
                {
                    Vertex v = _layers[i][j];
                    if (v.children.Count == 1)
                    {
                        if (v.children[0].isDummy && v.parents.Count == 0)
                        { // dummy becomes v, v is removed
                            MapProjectVertex[v.project] = v.dummy;
                            v.dummy.isDummy = false;
                            v.dummy.parents.Remove(v);
                            v.children.Clear();
                            _layers[i].RemoveAt(j--);
                        }
                    }
                }
            }
        }

        /******************************************************************************************
         *  STEP 6.5
         *  
         *  Rebuild vertex list, including dummies, to be used in drawing
         *  
         * 
         ******************************************************************************************/
        void RebuildVertexList()
        {
            foreach (Layer l in _layers)
            {
                foreach (Vertex v in l)
                {
                    vertices.Add(v);
                }
            }
        }

        /******************************************************************************************
         *  STEP 7
         *  
         *  Set X and Y coordinates based off layering
         *  
         * 
         ******************************************************************************************/
        void AssignCoordinates()
        {
            float x = 0f, y;
            
            // trivial assignment
            foreach (Layer l in _layers)
            {
                y = 0f;
                foreach (Vertex v in l)
                {
                    v.x = x;
                    v.y = y;
                    y += yStep;
                }
                x += xStep;
            }
            maxX = x;

            // using median to improve positioning
            for (int j = 0; j < _layers.Count - 1; j++)
            { // for each layer except last
                Layer l  = _layers[j];
                Layer lR = _layers[j + 1];

                for (int i = l.Count - 1; i > -1; i--)
                {
                    Vertex v = l[i];

                    // parent positioning
                    float median = MedianValue(v, lR, false);
                    if (i == l.Count - 1)
                    {
                        if (v.y < median)
                        {
                            v.y = median;
                        }
                    }
                    else
                    {
                        if (v.y < median)
                        {
                            if (median > (l[i + 1].y - yStep))
                            {
                                median = l[i + 1].y - yStep;
                            }
                            v.y = median;
                        }
                    }

                    // children positioning
                    /* not ready yet
                    int index = lR.FindLastIndex(k => v.children.Contains(k));
                    float desired = v.y + (v.children.Count-1) / 2f * yStep;
                    if (desired > (maxW * yStep))
                    { // we don't want to need the damn vertical scrollbar
                        desired = maxW * yStep;
                    }
                    while (v.children.Contains(lR[index]) && (index >= 0))
                    {
                        if (index == lR.Count - 1)
                        {
                            lR[index].y = desired;
                        }
                        else
                        {
                            if (desired > lR[index+1].y - yStep)
                            {
                                desired = lR[index + 1].y - yStep;
                            }
                            lR[index].y = desired;
                        }
                        desired -= yStep;
                        index--;
                    }*/
                }
            }
        }

        /******************************************************************************************
         * 
         * Transfer coordinates from Vertices to actual Projects
         * 
         * This cannot be called by any thread other than the Main Thread
         * 
         ******************************************************************************************/
        public void TransferCoordinates(List<ResearchProjectDef> projects)
        {
            FieldInfo xInfo = typeof(ResearchProjectDef).GetField("x", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo yInfo = typeof(ResearchProjectDef).GetField("y", BindingFlags.Instance | BindingFlags.NonPublic);
            Vertex v;

            foreach (ResearchProjectDef project in projects)
            {
                v = MapProjectVertex[project];
                xInfo.SetValue(project, v.x);
                yInfo.SetValue(project, v.y);
            }
        }
        
        /******************************************************************************************
         * 
         * Simple swap of vertices in a list.
         * 
         * 
         ******************************************************************************************/
        void SwapInList<T>(List<T> list, int indexA, int indexB)
        {
            T tmp = list[indexA];
            list[indexA] = list[indexB];
            list[indexB] = tmp;
        }


        /******************************************************************************************
         * 
         *  Debug method to print the contents of a list of research projects.
         * 
         * 
         ******************************************************************************************/
        void PrintList(List<ResearchProjectDef> list)
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
        void PrintTopologicalOrdering(List<ResearchProjectDef> list)
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
        void PrintLayerAndTightEdges(List<ResearchProjectDef> Layer, List<ResearchProjectDef> nextLayer, int index)
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
        #endregion
    }
}


