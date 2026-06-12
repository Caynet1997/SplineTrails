<h1>Spline tool script project in Flax Engine</h1>

<img align="center" src="Preview.png" width="1024px"/>

<h2>A set of simple scripts for extruding and instancing 3D meshes along a Spline. Implemented in C#.  <h2>

<h2>Spline Sampler</h2>
A base script sample some data alone the spline, used by another scripts.

<h2>Spline Extrude</h2>

<img align="center" src="Preview2.png" width="768px"/>
Spline extrude script could generate a StaticModel with a specific shape alone the spline. 
Useful to create tube or wire.

<h2>Spline Mesh</h2>

<img align="center" src="Preview1.png" width="768px"/>
The spline mesh script can generate a StaticModel that uses specific model mesh data to procedural generate any number of deformed meshes arranged along a spline, and finally combines them into one mesh to used by the StaticModel.
It is very helpful for creating scene with continuous arrangements, such as fences or sleeper tracks.

<h2>Train(Sample Script)</h2>
A script demonstrates how to make an actor move forward along a spline at any speed or how to get position or transform etc at any distance along spline.
