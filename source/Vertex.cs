/* Lucas Azevedo
 * 2017-03-25
 */

using System;
using System.Collections.Generic;

using RimWorld;
using Verse;

namespace OrganizedResearch
{
    class Vertex
    {
        public float x { get; set; }
        public float y { get; set; }

        public ResearchProjectDef project { get; set; }

        public List<Vertex> children { get; } = new List<Vertex>();
        public List<Vertex> parents  { get; } = new List<Vertex>();
        public Vertex dummy { get; set; } = null;
        public bool isDummy { get; set; }
        
        public Vertex(ResearchProjectDef project, bool isDummy)
        {
            this.project = project;
            this.isDummy = isDummy;
        }
    }
}
