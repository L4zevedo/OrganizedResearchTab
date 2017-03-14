# OrganizedResearchTab

When you use mods that add research projects, your Research Tab gets cluttered, sometimes with projects out of view. This mod presents a solution to that problem.

The main sorting criteria is dependency. Think of each column you see as a layer, organized in way that allows projects to appear more to the right than its prerequisites. Also, layers are internally sorted to minimize edge crossing (those lines that indicate dependency), in order to avoid a spaghetti look.

The problem of drawing that kind of structure in a aesthetically pleasing way is an old one and many computer scientists dedicated themselves to solving some aspects of it along the last few decades. I chose to follow what is known as the Sugiyama Framework, while the sorting part of it is a version of the Coffman-Graham algorithm.
