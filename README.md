# sever-extension-dotnet
Server extensions allow developers to change and extend the capabilities of ArcGIS Server map and 
image services. This project provides a template intended to bootstrap the development of both types of .NET-based 
server extensions, Server Object Extensions (SOEs) and Server Object Interceptors (SOIs). In general, this project limits itself to 
REST server extensions.

## Features
* SimplifyMapContentsSOI - A sample SOI that allows you to author a map with multiple layers but only exposes one layer in the map service REST resources to keep the web clients user experience simpler
     e.g. https://livefeeds2.arcgis.com/arcgis/rest/services/NFIE/NationalWaterModel_Medium_Anomaly/MapServer has only one layer in its root resources but the source map document (.mxd) has 5 layers representing details streams at varying level.
     In addition to that the SOI does following:
          (b) the [legend](http://livefeeds2.arcgis.com/arcgis/rest/services/NFIE/NationalWaterModel_Medium_Anomaly/MapServer/legend) is customized for REST output whereas the layer in the source map document authored with multi-field unique value renderer.
          (c) spatial query requests always get redirected to the layer with most details level of stream and returns only one stream feature (with highest order) even though there may be more than 1 streams are found.

## Instructions
1. Fork and then clone the repo. 
2. Run and try the examples.

## Requirements
* ArcGIS Enterprise (ArcGIS Server) 10.4
* ArcGIS Desktop 10.4
* ArcObjects SDK for .NET 10.4
* Microsoft Visual Studio

## Resources
* [About extending services](http://server.arcgis.com/en/server/latest/publish-services/windows/about-extending-services.htm)
* [ArcObjects Help for .NET](https://desktop.arcgis.com/en/arcobjects/latest/net/webframe.htm#f08861cf-c137-49d2-ade8-33aa4af63b1f.htm/)

## Issues
Find a bug or want to request a new feature?  Please let us know by submitting an issue.

## Contributing
Esri welcomes contributions from anyone and everyone. Please see our [guidelines for contributing](https://github.com/esri/contributing).

## Licensing
Copyright 2018 Esri

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

   http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

A copy of the license is available in the repository's [license.txt](/license.txt) file.

[](Esri Tags: ArcGIS Enterprise Server MapService SOI)
[](Esri Language: .Net)​