# Spatial Query and Binning Tool

This ArcGIS Pro add-in enables efficient spatial queries, allowing points to be binned into polygons and analyzed with a simple, user-friendly interface.

---

## Features

- **Point Selection**: Analyze points by intersecting them with polygons.
- **Polygon Binning**: Group points into chosen polygon layers (e.g., census tracts, parcels).
- **Save Results**: Export intersected polygons to a new feature class.
- **Summary Statistics**: View detailed metrics at the end of each run.

---

## How to Use

1. **Select Inputs**:
   - **Point Layer**: The layer containing source points.
   - **Binning Polygon Layer**: Polygons to group the points into.
   - **Parcels Layer**: Polygons to query for results.
2. **Optional**: Enable the "Save as New Feature Class" option to save results.
3. **Run the Analysis**: Click the **Run** button to execute the spatial query.
4. **View Results**: Check the output feature class and review statistics displayed in a message box.

---

## Example Statistics Output

```text
Total Points: 1,000  
Points With Parcels: 800  
Points Without Parcels: 200  
% Points With Parcels: 80.0%  
Output Feature Class: Results_YYYYMMDD_HHMMSS
