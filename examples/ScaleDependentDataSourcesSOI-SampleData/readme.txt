Steps to setup with the sample dataset and map found in this folder:
- Compile Visual Studio project 
- Copy datasource.json to c:\arcgisserver folder (as it is hardcoded in ScaleDependentDataSources.cs @ line#63)
- Open USDemograhic.mxd
- Publish that as a map service
- Open ArcGIS Server Manager
    - Use Add Extension button from Site | Extensions to upload ScaleDependentDataSources.soe from the Visual Studio project bin folder.
    - Open the map service property pages.
    - Switch to Capabilities tab.
    - Select Mapping (always enabled) option if it is not already selected
    - Check Allow per request modification of layer order and symbology check box under Dynamic Workspaces section.
        - Click on Add button and add a dynamic workspace of type 'File Geodatabase'
            - provide 'usdemo' as the 'Workspace ID' (as it is provided in the json file)
            - on 'Location' copy/paste the path of the File Geodatabase stored in this folder
    - Attach the 'ScaleDependentDataSources' to the map service from this page.
    - Save and restart the map service
- Once restarted, go to the map service REST end point
    - click on 'ArcGIS Online Map Viewer' link from the top
    - you should see US states are drawn 
    - zoom in one level on the map and you will see the same information is drawn at the county level.
      even though, there is only one layer in the map service


Reference:
- For more information on SOI, read [help here](http://server.arcgis.com/en/server/latest/publish-services/windows/about-extending-services.htm#ESRI_SECTION1_22386DF3305F42D4997F6F4301F8A8D5).