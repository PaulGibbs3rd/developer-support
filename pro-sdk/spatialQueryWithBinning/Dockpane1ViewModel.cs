using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Data.DDL; // For schema building (FieldDescription, FeatureClassDescription, etc.)

using ArcGIS.Core.Geometry;
using ArcGIS.Core.Internal.Geometry;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Extensions;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Mapping.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace createFeatureClassFromSpatialQuery
{
    internal class Dockpane1ViewModel : DockPane
    {
        private const string _dockPaneID = "createFeatureClassFromSpatialQuery_Dockpane1";
        private static readonly object lockObject = new object();

        #region Constructor / Show Logic

        protected Dockpane1ViewModel()
        {
            Heading = "Batch Query DockPane";
        }
       

        internal static void Show()
        {
            var pane = FrameworkApplication.DockPaneManager.Find(_dockPaneID);
            if (pane == null) return;
            var viewModel = pane as Dockpane1ViewModel;
            if (MapView.Active != null)
                viewModel?.LoadLayers();
            // Subscribe to MapViewInitialized, so we can load layers once the map is ready
            MapViewInitializedEvent.Subscribe(OnMapViewInitialized);
            pane.Activate();
        }

        private static void OnMapViewInitialized(MapViewEventArgs obj)
        {
            var viewModel = FrameworkApplication.DockPaneManager.Find(_dockPaneID) as Dockpane1ViewModel;
            viewModel?.LoadLayers();
        }

        protected override Task InitializeAsync()
        {
            if (MapView.Active != null)
                LoadLayers();
            return base.InitializeAsync();
        }

        #endregion

        #region Properties (Bound to XAML)

        private string _heading;
        public string Heading
        {
            get => _heading;
            set => SetProperty(ref _heading, value);
        }

        // --- Points Layers from the map ---
        private ObservableCollection<string> _mapPointLayers = new ObservableCollection<string>();
        public ObservableCollection<string> MapPointLayers
        {
            get => _mapPointLayers;
            set => SetProperty(ref _mapPointLayers, value);
        }

        private string _selectedFeatureLayerName;
        public string SelectedFeatureLayerName
        {
            get => _selectedFeatureLayerName;
            set
            {
                if (SetProperty(ref _selectedFeatureLayerName, value))
                {
                    // Whenever this changes, re-check if the button can run
                    (_runSpatialQueryCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private ObservableCollection<string> _mapPolygonLayers = new ObservableCollection<string>();
        public ObservableCollection<string> MapPolygonLayers
        {
            get => _mapPolygonLayers;
            set => SetProperty(ref _mapPolygonLayers, value);
        }

        private string _selectedPolygonLayerName;
        public string SelectedPolygonLayerName
        {
            get => _selectedPolygonLayerName;
            set
            {
                if (SetProperty(ref _selectedPolygonLayerName, value))
                {
                    (_runSpatialQueryCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }


        private ObservableCollection<string> _mapParcelLayers = new ObservableCollection<string>();
        public ObservableCollection<string> MapParcelLayers
        {
            get => _mapParcelLayers;
            set => SetProperty(ref _mapParcelLayers, value);
        }

        private string _selectedParcelsLayerName;
        public string SelectedParcelsLayerName
        {
            get => _selectedParcelsLayerName;
            set
            {
                if (SetProperty(ref _selectedParcelsLayerName, value))
                {
                    (_runSpatialQueryCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }


        // --- Hard-coded polygon binning layer choices (Tracts, Counties, States) ---
        private ObservableCollection<string> _polygonChoices;
        public ObservableCollection<string> PolygonChoices
        {
            get => _polygonChoices;
            set => SetProperty(ref _polygonChoices, value);
        }

        private string _selectedPolygonChoice;
        public string SelectedPolygonChoice
        {
            get => _selectedPolygonChoice;
            set
            {
                if (SetProperty(ref _selectedPolygonChoice, value))
                {
                    (_runSpatialQueryCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }



        // --- Whether to save the results to a new feature class ---
        private bool _saveAsNewFeatureClass;
        public bool SaveAsNewFeatureClass
        {
            get => _saveAsNewFeatureClass;
            set => SetProperty(ref _saveAsNewFeatureClass, value);
        }

        #endregion

        #region Commands

        private RelayCommand _runSpatialQueryCommand;
        public ICommand RunSpatialQueryCommand
            => _runSpatialQueryCommand ??
               (_runSpatialQueryCommand = new RelayCommand(async () => await RunSpatialQuery(),
                                                           () => CanRunSpatialQuery()));

        private bool CanRunSpatialQuery()
        {
            // Must have a valid points layer
            if (string.IsNullOrEmpty(SelectedFeatureLayerName))
                return false;

            // Must have a valid polygon choice
            if (string.IsNullOrEmpty(SelectedPolygonLayerName))
                return false;

            

            return true;
        }


        #endregion

        #region Main Logic
        int totalPoints = 0;
        int pointsWithResults = 0;
        /// <summary>
        /// Main method that replicates a "bin by polygon" approach:
        /// 1) Optionally create an empty output FC by cloning from Regrid.
        /// 2) Determine polygons that have points.
        /// 3) For each polygon, select points, then parcels, and append them to the output FC.
        /// 4) Report stats to the user.
        /// </summary>
        private async Task RunSpatialQuery()
        {
            try
            {
                // 1. Grab the user-chosen polygon layer from the map
                Map activeMap = MapView.Active.Map;
                FeatureLayer polygonLayer = activeMap.Layers
                    .OfType<FeatureLayer>()
                    .FirstOrDefault(fl => fl.Name == SelectedPolygonLayerName);
                if (polygonLayer == null)
                {
                    MessageBox.Show("Could not find the selected polygon layer in the map.");
                    return;
                }

                // 2. Possibly use Regr6id for parcels or a local parcels layer

                FeatureLayer localParcelsLayer = null;


                {
                    localParcelsLayer = activeMap.Layers
                        .OfType<FeatureLayer>()
                        .FirstOrDefault(fl => fl.Name == SelectedParcelsLayerName);
                    if (localParcelsLayer == null)
                    {
                        MessageBox.Show("Could not find a local parcels layer in the map. Stopping.");
                        return;
                    }
                }

                // 3. Create the output feature class if requested
                string newFcPath = null;
                string newFcName = $"Results_{DateTime.Now:yyyyMMdd_HHmmss}";
                string defaultGdb = Project.Current.DefaultGeodatabasePath;
                if (SaveAsNewFeatureClass)
                {
                    newFcPath = $"{defaultGdb}\\{newFcName}";
                    await QueuedTask.Run(() =>
                    {
                        using (var geodatabase = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(defaultGdb))))
                        {
                            bool created = CreateOutputFeatureClassFromLocalLayer(geodatabase, newFcName, localParcelsLayer);
                            if (!created)
                            {
                                MessageBox.Show("Could not create output FC from Regrid schema.");
                                newFcPath = null;
                            }
                        }
                    });
                }

                // 4. Grab the user-chosen points layer
                FeatureLayer pointsLayer = activeMap.Layers
                    .OfType<FeatureLayer>()
                    .FirstOrDefault(fl => fl.Name == SelectedFeatureLayerName);
                if (pointsLayer == null)
                {
                    MessageBox.Show("Could not find the selected points layer in the map.");
                    return;
                }


                // Step 6: Optimized sequential processing of binning polygons
                await QueuedTask.Run(() =>
                {
                    // Step 6.1: Ensure points are selected
                    pointsLayer.Select(null); // Select all points
                    var selectedPoints = pointsLayer.GetSelection();
                    if (selectedPoints.GetCount() == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("No points selected. Exiting.");
                        return;
                    }

                    // Step 6.2: Create a multipoint geometry from all selected points
                    System.Diagnostics.Debug.WriteLine("Creating multipoint geometry from selected points...");

                    List<Geometry> pointGeometries = new List<Geometry>();
                    using (RowCursor pointCursor = selectedPoints.Search())
                    {
                        while (pointCursor.MoveNext())
                        {
                            using (Feature pointFeature = pointCursor.Current as Feature)
                            {
                                if (pointFeature == null) continue;

                                Geometry pointGeometry = pointFeature.GetShape();
                                if (pointGeometry != null && !pointGeometry.IsEmpty)
                                {
                                    pointGeometries.Add(pointGeometry);
                                    totalPoints++;
                                }
                            }
                        }
                    }

                    Geometry multipointGeometry = GeometryEngine.Instance.Union(pointGeometries);
                    if (multipointGeometry == null || multipointGeometry.IsEmpty)
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to create multipoint geometry. Exiting.");
                        return;
                    }

                    polygonLayer.Select(new SpatialQueryFilter
                    {
                        FilterGeometry = multipointGeometry,
                        SpatialRelationship = SpatialRelationship.Intersects
                    });

                    var selectedPolygons = polygonLayer.GetSelection();
                    if (selectedPolygons.GetCount() == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("No binning polygons found. Exiting.");
                        return;
                    }

                    System.Diagnostics.Debug.WriteLine($"Found {selectedPolygons.GetCount()} binning polygons. Processing...");

                    // Step 6.3: Iterate through each binning polygon and collect results for saving later
                    var binningToResultsMap = new Dictionary<long, HashSet<long>>();

                    using (RowCursor polygonCursor = selectedPolygons.Search())
                    {
                        while (polygonCursor.MoveNext())
                        {
                            using (Feature polygonFeature = polygonCursor.Current as Feature)
                            {
                                if (polygonFeature == null) continue;

                                Geometry polygonGeometry = polygonFeature.GetShape();
                                if (polygonGeometry == null || polygonGeometry.IsEmpty) continue;

                                long binningOID = polygonFeature.GetObjectID();
                                System.Diagnostics.Debug.WriteLine($"Processing binning polygon OID: {binningOID}...");

                                // Select points within the current polygon
                                pointsLayer.Select(new SpatialQueryFilter
                                {
                                    FilterGeometry = polygonGeometry,
                                    SpatialRelationship = SpatialRelationship.Intersects
                                });

                                var pointsInPolygon = pointsLayer.GetSelection();
                                if (pointsInPolygon.GetCount() == 0)
                                {
                                    System.Diagnostics.Debug.WriteLine($"No points found in binning polygon OID: {binningOID}.");
                                    continue;
                                }

                                // Initialize a HashSet to store unique result polygon OIDs for this binning polygon
                                if (!binningToResultsMap.ContainsKey(binningOID))
                                {
                                    binningToResultsMap[binningOID] = new HashSet<long>();
                                }

                                // Step 6.4: Iterate through the points and collect intersecting result polygons
                                // Step 6.4: Collect intersecting result polygons using a batch query
                                System.Diagnostics.Debug.WriteLine($"Collecting result polygons for points in binning polygon OID: {binningOID}...");

                                // Collect all point geometries in the binning polygon
                                List<Geometry> pointsInPolygonGeometries = new List<Geometry>();
                                using (RowCursor pointsCursor = pointsInPolygon.Search())
                                {
                                    while (pointsCursor.MoveNext())
                                    {
                                        using (Feature pointFeature = pointsCursor.Current as Feature)
                                        {
                                            if (pointFeature == null) continue;

                                            Geometry pointGeometry = pointFeature.GetShape();
                                            if (pointGeometry == null || pointGeometry.IsEmpty) continue;

                                            pointsInPolygonGeometries.Add(pointGeometry);
                                        }
                                    }
                                }

                                // Create a multipoint geometry from all point geometries in the binning polygon
                                Geometry subsetMultipoint = GeometryEngine.Instance.Union(pointsInPolygonGeometries);
                                if (subsetMultipoint == null || subsetMultipoint.IsEmpty)
                                {
                                    System.Diagnostics.Debug.WriteLine($"No valid geometries found in binning polygon OID: {binningOID}.");
                                    continue;
                                }

                                // Perform a single spatial query for the multipoint against the results layer
                                var resultFilter = new SpatialQueryFilter
                                {
                                    FilterGeometry = subsetMultipoint,
                                    SpatialRelationship = SpatialRelationship.Intersects
                                };

                                using (RowCursor resultCursor = localParcelsLayer.Search(resultFilter))
                                {
                                    while (resultCursor.MoveNext())
                                    {
                                        long resultOID = resultCursor.Current.GetObjectID();
                                        binningToResultsMap[binningOID].Add(resultOID);
                                        pointsWithResults++;
                                    }
                                }

                            }
                        }
                    }

                    // Step 6.5: Write collected result polygons to the output feature class
                    System.Diagnostics.Debug.WriteLine("Writing collected result polygons to the output feature class...");

                    List<long> allResultOIDs = binningToResultsMap.Values.SelectMany(oids => oids).Distinct().ToList();

                    if (allResultOIDs.Count > 0 && !string.IsNullOrEmpty(newFcPath))
                    {
                        AppendParcelsToOutput(allResultOIDs, newFcPath, localParcelsLayer.GetFeatureClass());
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("No result polygons to write to the output feature class.");
                    }

                    // Step 8: Final statistics display
                    //int totalPoints = pointsLayer.GetFeatureClass().GetDefinition().GetCount(); // Total points in the points layer
                    int pointsWithParcels = binningToResultsMap.Values.Sum(set => set.Count);  // Count of points that found parcels
                    int pointsWithoutParcels = totalPoints - pointsWithResults;                // Count of points without parcels
                    int totalPolygonsProcessed = binningToResultsMap.Count;                    // Count of binning polygons processed

                    MessageBox.Show(
                        $"Statistics Summary:\n\n" +
                        $"Total Points: {totalPoints}\n" +
                        $"Points With Parcels: {pointsWithParcels}\n" +
                        $"Points Without Parcels: {pointsWithoutParcels}\n" +
                        $"% Points With Parcels: {(totalPoints > 0 ? ((double)pointsWithParcels / totalPoints * 100).ToString("0.##") : "0")}%\n" +
                        $"Total Binning Polygons Processed: {totalPolygonsProcessed}\n\n" +
                        $"Output Feature Class: {(string.IsNullOrEmpty(newFcPath) ? "Not Created" : newFcPath)}",
                        "Processing Complete"
                    );

                });

                


            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in RunSpatialQuery: {ex.Message}");
            }
        }


        #endregion

        #region Helpers

        
        /// <summary>
        /// Create an empty output feature class in the default GDB,
        /// cloning the "Regrid" schema. This parallels the "create_output_fc"
        /// logic from your Python script.
        /// </summary>
        private bool CreateOutputFeatureClassFromLocalLayer(Geodatabase geodatabase, string newFcName, FeatureLayer localParcelsLayer)
        {
            try
            {
                if (localParcelsLayer == null) return false;

                // Get the FeatureClass and Definition from the selected local layer
                using (FeatureClass localFeatureClass = localParcelsLayer.GetFeatureClass())
                {
                    if (localFeatureClass == null) return false;
                    FeatureClassDefinition def = localFeatureClass.GetDefinition();

                    // Build field descriptions
                    List<ArcGIS.Core.Data.DDL.FieldDescription> fieldDescriptions = new List<ArcGIS.Core.Data.DDL.FieldDescription>();

                    // Add an OID field
                    fieldDescriptions.Add(ArcGIS.Core.Data.DDL.FieldDescription.CreateObjectIDField());

                    // Copy fields except OID/geometry/global
                    foreach (Field fld in def.GetFields())
                    {
                        if (fld.FieldType == FieldType.OID || fld.FieldType == FieldType.Geometry || fld.FieldType == FieldType.GlobalID)
                            continue;

                        var fd = new ArcGIS.Core.Data.DDL.FieldDescription(fld.Name, fld.FieldType)
                        {
                            AliasName = fld.AliasName,
                            IsNullable = fld.IsNullable,
                            Length = fld.Length,
                            Precision = fld.Precision,
                            Scale = fld.Scale
                        };
                        fieldDescriptions.Add(fd);
                    }

                    // Clone geometry
                    ShapeDescription shapeDesc = new ShapeDescription(def);

                    // Build a FeatureClassDescription
                    var fcDesc = new ArcGIS.Core.Data.DDL.FeatureClassDescription(
                        newFcName,
                        fieldDescriptions,
                        shapeDesc);

                    var sb = new SchemaBuilder(geodatabase);
                    sb.Create(fcDesc);

                    if (!sb.Build())
                    {
                        var errs = sb.ErrorMessages;
                        MessageBox.Show("Failed building schema: " + string.Join("\n", errs));
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating output FC: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Optimized method to append parcels from a source feature class into the target feature class.
        /// </summary>
        private void AppendParcelsToOutput(List<long> parcelOIDs, string newFcPath, FeatureClass sourceFc)
        {
            if (parcelOIDs == null || parcelOIDs.Count == 0) return;
            if (string.IsNullOrEmpty(newFcPath)) return;

            try
            {
                // Extract the directory and feature class name from the path
                var gdbDir = System.IO.Path.GetDirectoryName(newFcPath);
                var fcName = System.IO.Path.GetFileName(newFcPath);

                // Open the target geodatabase and feature class
                using (var geodatabase = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbDir))))
                using (FeatureClass targetFc = geodatabase.OpenDataset<FeatureClass>(fcName))
                {
                    // Fetch field definitions for mapping source to target
                    var targetFields = targetFc.GetDefinition().GetFields();
                    var sourceFields = sourceFc.GetDefinition().GetFields();

                    // Map source fields to target fields by name
                    var fieldMapping = targetFields
                        .Where(f => f.FieldType != FieldType.OID && f.FieldType != FieldType.Geometry)
                        .ToDictionary(
                            targetField => targetField.Name,
                            targetField => sourceFields.FirstOrDefault(
                                sourceField => sourceField.Name.Equals(targetField.Name, StringComparison.OrdinalIgnoreCase)
                            )
                        );

                    // Batch processing for performance optimization
                    const int batchSize = 500; // Adjust batch size as needed
                    for (int i = 0; i < parcelOIDs.Count; i += batchSize)
                    {
                        var batchOIDs = parcelOIDs.Skip(i).Take(batchSize).ToList();
                        System.Diagnostics.Debug.WriteLine($"Processing batch of {batchOIDs.Count} parcels...");

                        var rowBuffers = new List<RowBuffer>();

                        // Query source features by batch OIDs
                        using (RowCursor sourceCursor = sourceFc.Search(new QueryFilter
                        {
                            WhereClause = $"{sourceFc.GetDefinition().GetObjectIDField()} IN ({string.Join(",", batchOIDs)})"
                        }))
                        {
                            while (sourceCursor.MoveNext())
                            {
                                using (Feature sourceFeature = sourceCursor.Current as Feature)
                                {
                                    if (sourceFeature == null) continue;

                                    // Validate geometry
                                    var sourceGeometry = sourceFeature.GetShape();
                                    if (sourceGeometry == null || sourceGeometry.IsEmpty )
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Skipping feature with invalid geometry: OID {sourceFeature.GetObjectID()}");
                                        continue;
                                    }

                                    // Create a row buffer for the target feature class
                                    var rowBuffer = targetFc.CreateRowBuffer();

                                    // Copy geometry
                                    rowBuffer[targetFc.GetDefinition().GetShapeField()] = sourceGeometry;

                                    // Copy attributes using field mapping
                                    foreach (var kvp in fieldMapping)
                                    {
                                        var targetField = kvp.Key;
                                        var sourceField = kvp.Value;

                                        if (sourceField != null)
                                        {
                                            rowBuffer[targetField] = sourceFeature[sourceField.Name];
                                        }
                                    }

                                    rowBuffers.Add(rowBuffer);
                                }
                            }
                        }

                        // Perform a batch insert into the target feature class
                        var editOp = new EditOperation();
                        editOp.Callback(context =>
                        {
                            foreach (var rowBuffer in rowBuffers)
                            {
                                using (var newRow = targetFc.CreateRow(rowBuffer))
                                {
                                    context.Invalidate(newRow);
                                }
                            }
                        }, targetFc);

                        if (!editOp.Execute())
                        {
                            System.Diagnostics.Debug.WriteLine("Batch append operation failed.");
                            System.Diagnostics.Debug.WriteLine(editOp.ErrorMessage);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Successfully appended {rowBuffers.Count} parcels in batch.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error appending parcels: {ex.Message}");
            }
        }







        #endregion

        #region Loading Point Layers

        private async void LoadLayers()
        {
            // Step 1: Gather data on the background (MCT) thread
            var pointNames = await QueuedTask.Run(() =>
            {
                if (MapView.Active == null) return new List<string>();

                var map = MapView.Active.Map;

                // All point layers
                var pointLayerNames = map.Layers
                    .OfType<FeatureLayer>()
                    .Where(fl =>
                    {
                        var fc = fl.GetFeatureClass();
                        if (fc == null) return false;

                        var geometryType = fc.GetDefinition().GetShapeType();
                        return geometryType == GeometryType.Point;
                    })
                    .Select(fl => fl.Name)
                    .ToList();

                return pointLayerNames;
            });

            // Step 2: Now set the ViewModel properties on the UI thread
            MapPointLayers = new ObservableCollection<string>(pointNames);

            // Step 3: Gather polygon layers for binning
            var polygonNames = await QueuedTask.Run(() =>
            {
                if (MapView.Active == null) return new List<string>();

                var map = MapView.Active.Map;

                // All polygon layers
                var polygonLayerNames = map.Layers
                    .OfType<FeatureLayer>()
                    .Where(fl =>
                    {
                        var fc = fl.GetFeatureClass();
                        if (fc == null) return false;

                        var geometryType = fc.GetDefinition().GetShapeType();
                        return geometryType == GeometryType.Polygon;
                    })
                    .Select(fl => fl.Name)
                    .ToList();

                return polygonLayerNames;
            });

            MapPolygonLayers = new ObservableCollection<string>(polygonNames);

            // Step 4: Populate local parcel layers
            var parcelNames = await QueuedTask.Run(() =>
            {
                if (MapView.Active == null) return new List<string>();

                var map = MapView.Active.Map;

                // All polygon layers for parcels
                var parcelLayerNames = map.Layers
                    .OfType<FeatureLayer>()
                    .Where(fl =>
                    {
                        var fc = fl.GetFeatureClass();
                        if (fc == null) return false;

                        var geometryType = fc.GetDefinition().GetShapeType();
                        return geometryType == GeometryType.Polygon;
                    })
                    .Select(fl => fl.Name)
                    .ToList();

                return parcelLayerNames;
            });

            MapParcelLayers = new ObservableCollection<string>(parcelNames);
        }

        #endregion
    }

    /// <summary>
    /// Button implementation to show the DockPane.
    /// </summary>
    internal class Dockpane1_ShowButton : Button
    {
        protected override void OnClick()
        {
            Dockpane1ViewModel.Show();
        }
    }
}
