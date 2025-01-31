Overview
This ArcGIS Pro add-in performs advanced spatial queries by iterating through point and polygon layers in the map, applying spatial binning, and saving results to a new feature class. It is designed for workflows involving point data and polygon features where the goal is to analyze spatial relationships efficiently.

The program is implemented as a dockable pane in ArcGIS Pro, allowing users to interactively select layers and configure options.

Features
Layer Selection: Users can select point layers, polygon binning layers, and parcel layers.
Spatial Query and Binning: The program:
Selects points from the point layer.
Identifies intersecting polygons from the binning layer.
Collects intersecting polygons from a parcels layer for each binning polygon.
Output Feature Class Creation: The program optionally creates a new feature class to store the results.
Statistics Summary: Displays the total number of points processed, points with results, points without results, and the percentage of points with results.
How It Works
Initialization: The tool initializes the list of layers in the active map for user selection.
Spatial Query Execution:
Points are selected, and a multipoint geometry is created.
Intersecting polygons from the binning layer are identified.
For each binning polygon, points within the polygon are identified, and intersecting polygons from the parcels layer are collected.
Result Storage:
All intersecting polygons are saved to a new feature class, if specified.
Statistics Display: At the end of the process, a summary of the analysis is displayed.
Installation
Clone this repository to your local machine.
Open the solution in Visual Studio with the ArcGIS Pro SDK installed.
Build the project to generate the .esriAddInX file.
Install the add-in by double-clicking the generated file or using the Add-In Manager in ArcGIS Pro.
Usage
Open the dockpane from the add-in menu.
Select:
A point layer as the source layer.
A polygon layer as the binning layer.
A parcels layer for querying intersecting polygons.
Choose whether to save results as a new feature class.
Run the tool by clicking the "Run" button.
View the results in the map and review the summary statistics displayed in a message box.
Configuration Options
Save as New Feature Class: If enabled, the program creates a new feature class to store the results in the default geodatabase.
Key Methods and Code Flow
1. Layer Initialization
Populates dropdowns for points, binning polygons, and parcels layers from the map.
2. Run Spatial Query
Main entry point for the program.
Implements the spatial query logic:
Select points.
Identify intersecting polygons.
Collect results.
3. Output Feature Class Creation
Creates a feature class schema matching the parcels layer if results are saved.
4. Append Results
Appends polygons from the parcels layer to the output feature class.
5. Statistics Display
Computes and shows summary statistics for the query.

Dependencies
ArcGIS Pro SDK: Required to build and run the add-in.
.NET Framework: Used for development.
Known Issues
Large datasets may cause performance bottlenecks. Consider reducing the number of features processed in a single batch.
