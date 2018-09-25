Data source: ftp://ftp.ncep.noaa.gov/pub/data/nccf/com/nwm/prod/ 


Steps to setup with the sample dataset and map found in this folder:
- Compile Visual Studio project 
- Copy SimplifyMapContentsSOI.json to c:\arcgisserver folder (as it is hardcoded in the SOI in SimplifyMapContents.cs @ line#49)
- Open NationalWaterModel_Short_Anomaly_Sample.mxd
- Publish that as a map service
- Open ArcGIS Server Manager
    - Use Add Extension button from Site | Extensions to upload SimplifyMapContentsSOI.soe from the Visual Studio project bin folder.
    - Open the map service property pages.
    - Switch to Capabilities tab.
    - Select Mapping (always enabled) option if it is not already selected
    - Uncheck Allow per request modification of layer order and symbology check box under Dynamic Workspaces section.
    - Attach the 'SimplifyMapContentsSOI' to the map service from this page.
    - Save and restart the map service
- Once restarted, go to the map service REST end point and make sure, it advertises:
   - There is only 1 layer (instead of 5) in the service root resources, and
   - Time in the root resources.
   - Navigate to MapServer/legend resources and you should see a cleaner version of legend (that is how it is defined in SimplifyMapContentsSOI.json) unlike what you see in ArcMap when the mxd is opened.


Reference:
- For more information on SOI, read [help here](http://server.arcgis.com/en/server/latest/publish-services/windows/about-extending-services.htm#ESRI_SECTION1_22386DF3305F42D4997F6F4301F8A8D5).
- [Temporal data in separate tables](http://desktop.arcgis.com/en/arcmap/10.3/map/time/temporal-data-in-separate-tables.htm)