// Copyright 2016 ESRI
// 
// All rights reserved under the copyright laws of the United States
// and applicable international laws, treaties, and conventions.
// 
// You may freely redistribute and use this sample code, with or
// without modification, provided you include the original copyright
// notice and use restrictions.
// 

using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.esriSystem;
using ESRI.ArcGIS.Server;
using ESRI.ArcGIS.SOESupport;
using ESRI.ArcGIS.SOESupport.SOI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;


namespace AggregateLayerSOI
{
    [ComVisible(true)]
    [Guid("95529bc7-aeb7-4054-ad86-0adf889d2ac6")]
    [ClassInterface(ClassInterfaceType.None)]
    [ServerObjectInterceptor("MapServer",
        Description = "SOI to visualize layer with summary statistics/aggregated results from an observation table.",
        DisplayName = "Vizualize Layer with Aggregated Results",
        Properties = "")]
    public class AggregateLayerSOI : IServerObjectExtension, IRESTRequestHandler
    {
        private string _soiName;
        private IServerObjectHelper _soHelper;
        private ServerLogger _serverLog;
        private IServerObject _serverObject;
        RestSOIHelper _restServiceSOI;

        private bool _isVisualizeAggregatedResultEnabled = true;

        private String _configurationFilePath = "C:\\arcgisserver\\aggregate_layer_info.json";

        private long _targetLayerID;
        private string[] _aggregatedFunctions;
        private List<string> _virtualAggregatedFields = new List<string>();
        private string _valueFieldName = string.Empty;
        private string _valueFieldAliasName = string.Empty;
        private string _SQLOneAggregationTemplateWithTime;
        private string _SQLOneAggregationTemplateWOTime;
        private string _SQLBareBone;
        private string _SQLAllAggregationsWOTime;
        private string _SQLAllAggregationsTemplateWithTime;
        private string _defaultDynamicLayerJSONasString;
        private IDictionary<string, object> _dynamicLayerTemplate = null;
        private string _aggrVirtualFieldTemplate = "{{ \"name\": \"{0}_{1}\", \"type\": \"esriFieldTypeDouble\", \"alias\": \"{2} {3}\", \"domain\": null }}";
        private string _serviceTimeInfoJSONasString;
        private string _layerTimeInfoJSONasString;
        private Dictionary<string, string> _aggregationAliasDict = new Dictionary<string, string>
                                                                    {
                                                                        { "MAX",   "Maximum" },
                                                                        { "MIN",   "Minimum" },
                                                                        { "AVG",   "Average" },
                                                                        { "COUNT", "Count"   },
                                                                        { "SUM",   "Total"   }
                                                                    };

        public AggregateLayerSOI()
        {
            _soiName = this.GetType().Name;
        }

        private void InitFiltering()
        {
            _restServiceSOI.FilterMap = new Dictionary<RestHandlerOpCode, RestFilter>
            {
                { RestHandlerOpCode.Root, new RestFilter
                                            { 
                                                PreFilter = null, 
                                                PostFilter = PostFilterRESTRoot
                                            } },
                { RestHandlerOpCode.RootExport, new RestFilter
                                            { 
                                                PreFilter = PreFilterExport, 
                                                PostFilter = null 
                                            } },
                { RestHandlerOpCode.RootFind, new RestFilter
                                            { 
                                                PreFilter = null, 
                                                PostFilter = null 
                                            } },
                { RestHandlerOpCode.RootGenerateKml, new RestFilter
                                            { 
                                                PreFilter = null, 
                                                PostFilter = null 
                                            } },
                { RestHandlerOpCode.RootIdentify, new RestFilter
                                            { 
                                                PreFilter = null, 
                                                PostFilter = null 
                                            } },
                { RestHandlerOpCode.RootLayers, new RestFilter 
                                            { 
                                                PreFilter = null, 
                                                PostFilter = PostFilterRootLayers
                                            } },
                { RestHandlerOpCode.RootLegend, new RestFilter
                                            { 
                                                PreFilter = PreFilterRootLegend, 
                                                PostFilter = null
                                            } },
                { RestHandlerOpCode.LayerRoot, new RestFilter 
                                            { 
                                                PreFilter = null, 
                                                PostFilter = PostFilterLayerRoot 
                                            } },
                { RestHandlerOpCode.LayerGenerateRenderer, new RestFilter 
                                            { 
                                                PreFilter = PreFilterGenerateRenderer, 
                                                PostFilter = null 
                                            } },
                { RestHandlerOpCode.LayerQuery, new RestFilter
                                            { 
                                                PreFilter = PreFilterLayerQuery, 
                                                PostFilter = null 
                                            } },
                { RestHandlerOpCode.LayerQueryRelatedRecords, new RestFilter 
                                            { 
                                                PreFilter = null, 
                                                PostFilter = null 
                                            } },
                { RestHandlerOpCode.DefaultNoOp, new RestFilter
                                            {
                                                PreFilter = PreFilterOther,
                                                PostFilter = null 
                                            } }
            };
        }
        
        public void Init(IServerObjectHelper pSOH)
        {
            System.Diagnostics.Debugger.Launch();

            try
            {
                _soHelper = pSOH;
                _serverLog = new ServerLogger();
                _serverObject = pSOH.ServerObject;

                _restServiceSOI = new RestSOIHelper(_soHelper);

                InitFiltering();

                JsonObject aggregatedLayerConfigInfo = ReadConfigurationFile(_configurationFilePath);
                if (aggregatedLayerConfigInfo == null)
                {
                    _serverLog.LogMessage(ServerLogger.msgType.error, _soiName + ".init()", 500, "Configuration file does not exist at " + _configurationFilePath);
                    _isVisualizeAggregatedResultEnabled = false;
                }
                else
                {
                    if (!InitSetup(aggregatedLayerConfigInfo))
                    {
                        _serverLog.LogMessage(ServerLogger.msgType.error, _soiName + ".init()", 500, "One or more required properties are missing from the configuration file exists at " + _configurationFilePath);
                        _isVisualizeAggregatedResultEnabled = false;
                    }
                }

                JavaScriptSerializer sr = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                _dynamicLayerTemplate = sr.DeserializeObject(_defaultDynamicLayerJSONasString) as IDictionary<string, object>;
                sr = null;

                _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".init()", 200, "Initialized " + _soiName + " SOI.");
            }
            catch (Exception e)
            {
                _serverLog.LogMessage(ServerLogger.msgType.error, _soiName + ".init()", 500, "Exception: " + e.Message + " in " + e.StackTrace);
            }
        }

        public void Shutdown()
        {
            _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".init()", 200, "Shutting down " + _soiName + " SOI.");
        }


        #region REST interceptors

        public string GetSchema()
        {
            try
            {
                IRESTRequestHandler restRequestHandler = _restServiceSOI.FindRequestHandlerDelegate<IRESTRequestHandler>();
                if (restRequestHandler == null)
                    return null;

                return restRequestHandler.GetSchema();
            }
            catch (Exception e)
            {
                _serverLog.LogMessage(ServerLogger.msgType.error, _soiName + ".GetSchema()", 500, "Exception: " + e.Message + " in " + e.StackTrace);
                return null;
            }
        }

        private byte[] FilterRESTRequest(
                                          RestHandlerOpCode opCode,
                                          RestRequestParameters restInput,
                                          out string responseProperties)
        {
            try
            {
                responseProperties = null;
                IRESTRequestHandler restRequestHandler = _restServiceSOI.FindRequestHandlerDelegate<IRESTRequestHandler>();
                if (restRequestHandler == null)
                    throw new RestErrorException("Service handler not found");

                RestFilter restFilterOp = _restServiceSOI.GetFilter(opCode);

                if (null != restFilterOp && null != restFilterOp.PreFilter)
                    restInput = restFilterOp.PreFilter(restInput);

                byte[] response =
                    restRequestHandler.HandleRESTRequest(restInput.Capabilities, restInput.ResourceName, restInput.OperationName, restInput.OperationInput,
                        restInput.OutputFormat, restInput.RequestProperties, out responseProperties);

                if (null == restFilterOp || null == restFilterOp.PostFilter)
                    return response;

                string newResponseProperties;
                var newResponse = restFilterOp.PostFilter(restInput, response, responseProperties, out newResponseProperties);
                responseProperties = newResponseProperties;

                return newResponse;
            }
            catch (RestErrorException restException)
            {
                // pre- or post- filters can throw restException with the error JSON output in the Message property.
                // we catch them here and return JSON response.
                responseProperties = "{\"Content-Type\":\"text/plain;charset=utf-8\"}";
                //catch and return a JSON error from the pre- or postFilter.
                return System.Text.Encoding.UTF8.GetBytes(restException.Message);
            }
        }

        private RestHandlerOpCode GetHandlerOpCode(string resourceName, string operationName)
        {
            RestHandlerOpCode opCode = RestSOIHelper.GetHandlerOpCode(resourceName, operationName);

            if (opCode != RestHandlerOpCode.DefaultNoOp)
                return opCode;

            // The code below deals with the custom REST operation codes. This is required to enable REST request filtering for custom SOEs.
            // In this example the switch statement simply duplicates RestSOIHelper.GetHandlerOpCode() call.

            // If you don't plan to support filtering for any custom SOEs, remove code below until the end of the method.
            // If you want to support filtering for custom SOEs, modify the code below to match your needs.

            var resName = resourceName.TrimStart('/'); //remove leading '/' to prevent empty string at index 0
            var resourceTokens = (resName ?? "").ToLower().Split('/');
            string opName = (operationName ?? "").ToLower();

            switch (resourceTokens[0])
            {
                case "":
                    switch (opName)
                    {
                        case "":
                            return RestHandlerOpCode.Root;
                        case "export":
                            return RestHandlerOpCode.RootExport;
                        case "find":
                            return RestHandlerOpCode.RootFind;
                        case "identify":
                            return RestHandlerOpCode.RootIdentify;
                        case "generatekml":
                            return RestHandlerOpCode.RootGenerateKml;
                        default:
                            return RestHandlerOpCode.DefaultNoOp;
                    }
                case "layers":
                    {
                        var tokenCount = resourceTokens.GetLength(0);
                        if (1 == tokenCount)
                            return RestHandlerOpCode.RootLayers;
                        if (2 == tokenCount)
                            switch (opName)
                            {
                                case "":
                                    return RestHandlerOpCode.LayerRoot;
                                case "query":
                                    return RestHandlerOpCode.LayerQuery;
                                case "queryRelatedRecords":
                                    return RestHandlerOpCode.LayerQueryRelatedRecords;
                                case "generaterenderer":
                                    return RestHandlerOpCode.LayerGenerateRenderer;
                                default:
                                    return RestHandlerOpCode.DefaultNoOp;
                            }
                    }
                    break;
                case "legend":
                    return RestHandlerOpCode.RootLegend;
                default:
                    return RestHandlerOpCode.DefaultNoOp;
            }
            return RestHandlerOpCode.DefaultNoOp;
        }

        public byte[] HandleRESTRequest(string capabilities, string resourceName, string operationName,
            string operationInput, string outputFormat, string requestProperties, out string responseProperties)
        {
            try
            {
                responseProperties = null;
                _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".HandleRESTRequest()",
                    200, "Request received in Layer Access SOI for handleRESTRequest");

                var restInput = new RestRequestParameters
                {
                    Capabilities = capabilities,
                    ResourceName = resourceName,
                    OperationName = operationName,
                    OperationInput = operationInput,
                    OutputFormat = outputFormat,
                    RequestProperties = requestProperties
                };

                var opCode = GetHandlerOpCode(restInput.ResourceName, restInput.OperationName);
                return FilterRESTRequest(opCode, restInput, out responseProperties);
            }
            catch (Exception e)
            {
                _serverLog.LogMessage(ServerLogger.msgType.error, _soiName + ".HandleRESTRequest()", 500, "Exception: " + e.Message + " in " + e.StackTrace);
                throw;
            }
        }

        
        #endregion


        #region REST Pre-filters

        private RestRequestParameters PreFilterExport(RestRequestParameters restInput)
        {
            if (!_isVisualizeAggregatedResultEnabled)
                return restInput; 
            
            JavaScriptSerializer sr = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var operationInputJson = sr.DeserializeObject(restInput.OperationInput) as IDictionary<string, object>;
            if (!operationInputJson.ContainsKey("dynamicLayers"))
                return restInput;

            var jsDynLyrArray = (object[])operationInputJson["dynamicLayers"];

            // Step 2a: finding the input dynamic layer whose source would be replaced
            IDictionary<string, object> jsTargetDL = FindDynamicLayerByID(jsDynLyrArray, _targetLayerID);

            if (jsTargetDL == null)
                return restInput;

            /******* SOI to view aggregated result *****
             * Step 2b: determining whether aggregate layer needs to be used when
             *          1. a class breaks renderer with one of aggregated_fields is specified  AND
             *          2. time range is provided
             *******/
            bool switchToAggregateLayer = false;
            string stime = null, etime = null;
            string agrtFunction = GetAggregateFunctionFromDrawingInfo(jsTargetDL);

            if (agrtFunction != null)
                switchToAggregateLayer = GetTimeRange(operationInputJson, ref stime, ref etime);

            if (switchToAggregateLayer)
            {
                string sql = string.Format(_SQLOneAggregationTemplateWithTime, stime, etime, agrtFunction, _valueFieldName);
                                          //_SQLOneAggregationTemplateWithTime contains {0} = start-time; {1} = end-time;
                                          //                      {2} = aggregated-function-name; {3} = value-field-name;
                var jsSrc = _dynamicLayerTemplate["source"] as IDictionary<string, object>;
                var jsDS = jsSrc["dataSource"] as IDictionary<string, object>;
                jsDS["query"] = sql;
                jsTargetDL["source"] = jsSrc;
                restInput.OperationInput = sr.Serialize(operationInputJson);
            }

            return restInput;
        }

        private RestRequestParameters PreFilterGenerateRenderer(RestRequestParameters restInput)
        {
            if (!_isVisualizeAggregatedResultEnabled)
                return restInput; 
            
            if (restInput.ResourceName.Equals(string.Format("layers/{0}", _targetLayerID), StringComparison.CurrentCultureIgnoreCase))
            {
                JavaScriptSerializer sr = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var operationInputJson = sr.DeserializeObject(restInput.OperationInput) as IDictionary<string, object>;

                string agrtFunction = GetAggregateFunctionFromGenerateRenderer(operationInputJson);
                string sql = string.Format(_SQLOneAggregationTemplateWOTime, "", "", agrtFunction, _valueFieldName); //the format string does not have {0} and {1}

                var jsDL = _dynamicLayerTemplate;
                var jsSrc = jsDL["source"] as IDictionary<string, object>;
                var jsDS = jsSrc["dataSource"] as IDictionary<string, object>;
                jsDS["query"] = sql;

                operationInputJson.Add("layer", jsDL);

                restInput.ResourceName = "dynamicLayer"; //switching to dynamic layers
                restInput.OperationInput = sr.Serialize(operationInputJson);
            }
            return restInput;
        }

        private RestRequestParameters PreFilterOther(RestRequestParameters restInput)
        {
            if (!_isVisualizeAggregatedResultEnabled)
                return restInput; 
            
            if (restInput.ResourceName.Equals("dynamicLayer", StringComparison.CurrentCultureIgnoreCase))
            {
                JavaScriptSerializer sr = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var operationInputJson = sr.DeserializeObject(restInput.OperationInput) as IDictionary<string, object>;

                // make sure the source of dynamicLayer defined in the operationInput points to the _targetLayerId
                if (!operationInputJson.ContainsKey("layer"))
                    return restInput;

                var jsDynLyrArray = new object[] { operationInputJson["layer"] }; //putting it in an array to pass it to FindDynamicLayerByID function
                IDictionary<string, object> jsTargetDL = FindDynamicLayerByID(jsDynLyrArray, _targetLayerID);
                if (jsTargetDL == null)
                    return restInput;

                string opNm = restInput.OperationName.Trim().ToLower();
                var jsSrc = _dynamicLayerTemplate["source"] as IDictionary<string, object>;
                var jsDS = jsSrc["dataSource"] as IDictionary<string, object>;
                string agrtFunction = null, sql = null;
                switch (opNm)
                {
                    case "": //dynamic layer resources
                        jsDS["query"] = _SQLBareBone;
                        jsTargetDL["source"] = jsSrc;
                        break;
                    case "generaterenderer":
                        agrtFunction = GetAggregateFunctionFromGenerateRenderer(operationInputJson);
                        sql = string.Format(_SQLOneAggregationTemplateWOTime, "", "", agrtFunction, _valueFieldName); //the format string does not have {0} and {1}
                        jsDS["query"] = sql;
                        jsTargetDL["source"] = jsSrc;
                        break;
                    case "query":
                        string stime = null, etime = null;

                        //only when time is enabled or aggregated field is used in Where and/or outStats, get aggregated results for that given time winodw
                        //else get attribute from the existing mapLayer
                        bool hasTime = GetTimeRange(operationInputJson, ref stime, ref etime);
                        bool containsAggregatedField = ContainsAggregatedField(operationInputJson);
                        if (hasTime || containsAggregatedField)
                        {
                            if (hasTime)
                                sql = string.Format(_SQLAllAggregationsTemplateWithTime, stime, etime);
                            else
                                sql = _SQLAllAggregationsWOTime;

                            jsDS["query"] = sql;
                            jsTargetDL["source"] = jsSrc;

                            //Removing the value field that can't be part of the aggregated result
                            if (operationInputJson.ContainsKey("outFields"))
                            {
                                string outFields = operationInputJson["outFields"].ToString();
                                if (!outFields.Equals("*"))
                                {
                                    List<string> outFieldsList = outFields.Split(',').ToList<string>();
                                    string newOutFields = "";
                                    foreach (var f in outFieldsList)
                                    {
                                        if (f.Equals(_valueFieldName, StringComparison.CurrentCultureIgnoreCase))
                                            continue;

                                        newOutFields += (", " + f);
                                    }

                                    operationInputJson["outFields"] = newOutFields.Substring(1);
                                }
                            }
                        }
                        else //remove aggregated fields from the outFields
                        {
                            if (operationInputJson.ContainsKey("outFields"))
                            {
                                string outFields = operationInputJson["outFields"].ToString();
                                if (!outFields.Equals("*"))
                                {
                                    List<string> outFieldsList = outFields.Split(',').ToList<string>();
                                    string newOutFields = "";
                                    foreach (var f in outFieldsList)
                                    {
                                        if (_virtualAggregatedFields.Exists(vf => vf.Equals(f, StringComparison.CurrentCultureIgnoreCase)))
                                            continue;

                                        newOutFields += (", " + f);
                                    }

                                    operationInputJson["outFields"] = newOutFields.Substring(1);
                                }
                            }
                        }
                        break;
                    default:
                        break;
                }

                restInput.OperationInput = sr.Serialize(operationInputJson);
            }

            return restInput;
        }

        private RestRequestParameters PreFilterLayerQuery(RestRequestParameters restInput)
        {
            if (!_isVisualizeAggregatedResultEnabled)
                return restInput; 
            
            if (!restInput.ResourceName.Equals("layers/" + _targetLayerID, StringComparison.CurrentCulture))
                return restInput;

            JavaScriptSerializer sr = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var operationInputJson = sr.DeserializeObject(restInput.OperationInput) as IDictionary<string, object>;

            string stime = null, etime = null;

            //only when time is enabled, get aggregated results for that given time winodw
            //else get attribute from the existing mapLayer
            bool hasTime = GetTimeRange(operationInputJson, ref stime, ref etime);
            bool containsAggregatedField = ContainsAggregatedField(operationInputJson);
            if (hasTime || containsAggregatedField)
            {
                restInput.ResourceName = "dynamicLayer"; //switching to dynamic layers
                var jsDL = _dynamicLayerTemplate;
                var jsSrc = jsDL["source"] as IDictionary<string, object>;
                var jsDS = jsSrc["dataSource"] as IDictionary<string, object>;
                var sql = string.Format(_SQLAllAggregationsTemplateWithTime, stime, etime);
                jsDS["query"] = sql;

                operationInputJson.Add("layer", jsDL);

                //removing the value field, since it can't be part of any aggregated result
                if (operationInputJson.ContainsKey("outFields"))
                {
                    string outFields = operationInputJson["outFields"].ToString();
                    if (!outFields.Equals("*"))
                    {
                        List<string> outFieldsList = outFields.Split(',').ToList<string>();
                        string newOutFields = "";
                        foreach (var f in outFieldsList)
                        {
                            if (f.Equals(_valueFieldName, StringComparison.CurrentCultureIgnoreCase))
                                continue;

                            newOutFields += (", " + f);
                        }

                        operationInputJson["outFields"] = newOutFields.Substring(1);
                    }
                }
            }
            else //remove aggregated fields from the outFields
            {
                if (operationInputJson.ContainsKey("outFields"))
                {
                    string outFields = operationInputJson["outFields"].ToString();
                    if (!outFields.Equals("*"))
                    {
                        List<string> outFieldsList = outFields.Split(',').ToList<string>();
                        string newOutFields = "";
                        foreach (var f in outFieldsList)
                        {
                            if (_virtualAggregatedFields.Exists(vf => vf.Equals(f, StringComparison.CurrentCultureIgnoreCase)))
                                continue;

                            newOutFields += (", " + f);
                        }

                        operationInputJson["outFields"] = newOutFields.Substring(1);
                    }
                }

                if (operationInputJson.ContainsKey("orderByFields"))
                    operationInputJson["orderByFields"] = _valueFieldName;

            }

            restInput.OperationInput = sr.Serialize(operationInputJson);
            return restInput;
        }

        private bool ContainsAggregatedField(IDictionary<string, object> operationInputJson)
        {
            bool containsAggregatedFieldName = false;
            if (operationInputJson.ContainsKey("where"))
            {
                string where = operationInputJson["where"].ToString().ToUpper();
                foreach (var vf in _virtualAggregatedFields)
                {
                    if (where.Contains(vf.ToUpper()))
                        return true;
                }
            }

            if (operationInputJson.ContainsKey("outStatistics"))
            {
                var outStats = operationInputJson["outStatistics"] as object[];
                foreach (var item in outStats)
                {
                    var os = item as IDictionary<string, object>;
                    string osf = os["onStatisticField"].ToString();
                    foreach (var vf in _virtualAggregatedFields)
                    {
                        if (vf.Equals(osf, StringComparison.CurrentCultureIgnoreCase))
                            return true;
                    }
                }
            }

            return containsAggregatedFieldName;
        }

        private RestRequestParameters PreFilterRootLegend(RestRequestParameters restInput)
        {
            if (!_isVisualizeAggregatedResultEnabled)
                return restInput; 
            
            JavaScriptSerializer sr = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var operationInputJson = sr.DeserializeObject(restInput.OperationInput) as IDictionary<string, object>;

            if (!operationInputJson.ContainsKey("dynamicLayers"))
                return restInput;

            // make sure the source of dynamicLayer defined in the operationInput points to the _targetLayerId
            var jsDynLyrArray = (object[])operationInputJson["dynamicLayers"];
            IDictionary<string, object> jsTargetDL = FindDynamicLayerByID(jsDynLyrArray, _targetLayerID);
            if (jsTargetDL != null)
            {
                string agrtFunction = GetAggregateFunctionFromDrawingInfo(jsTargetDL);
                if (agrtFunction != null)
                {
                    var jsSrc = _dynamicLayerTemplate["source"] as IDictionary<string, object>;
                    var jsDS = jsSrc["dataSource"] as IDictionary<string, object>;

                    string sql = string.Format(_SQLOneAggregationTemplateWOTime, "", "", agrtFunction, _valueFieldName); //the format string does not have {0} and {1}
                    jsDS["query"] = sql;
                    jsTargetDL["source"] = jsSrc;
                }
            }

            restInput.OperationInput = sr.Serialize(operationInputJson);
            return restInput;
        }

        #endregion


        #region REST Post-filters

        private byte[] PostFilterRESTRoot(RestRequestParameters restInput, byte[] responseBytes, string responseProperties, out string newResponseProperties)
        {
            newResponseProperties = responseProperties;

            if (!_isVisualizeAggregatedResultEnabled)
                return responseBytes;

            try
            {
                String originalResponse = System.Text.Encoding.UTF8.GetString(responseBytes);
                JavaScriptSerializer sr = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var jsonResObj = sr.DeserializeObject(originalResponse) as IDictionary<string, object>;

                // Filter for 'contents' tag
                var contentsJO = jsonResObj["contents"] as IDictionary<string, object>;
                AddTimeInfo(contentsJO);

                var layersJA = contentsJO["layers"] as object[];
                var allResourcesJA = jsonResObj["resources"] as object[];
                var layersRJO = allResourcesJA.FirstOrDefault(e =>
                {
                    var jo = e as IDictionary<string, object>;
                    if (!jo.ContainsKey("name"))
                        return false;
                    var name = jo["name"].ToString();
                    return ("layers" == name);
                }) as IDictionary<string, object>;

                var tablesRJO = allResourcesJA.FirstOrDefault(e =>
                {
                    var jo = e as IDictionary<string, object>;
                    if (!jo.ContainsKey("name"))
                        return false;
                    var name = jo["name"].ToString();
                    return ("tables" == name);
                }) as IDictionary<string, object>;

                var legendRJO = allResourcesJA.FirstOrDefault(e =>
                {
                    var jo = e as IDictionary<string, object>;
                    if (!jo.ContainsKey("name"))
                        return false;
                    var name = jo["name"].ToString();
                    return ("legend" == name);
                }) as IDictionary<string, object>;

                //filter and replace layers
                if (null != layersRJO)
                {
                    // Filter for 'resources -> layers -> resources' tag
                    var layerResourceJA = layersRJO["resources"] as object[];

                    foreach (var item in layerResourceJA)
                    {
                        var jsLayer = item as IDictionary<string, object>;
                        var jsContent = jsLayer["contents"] as IDictionary<string, object>;
                        if ((int)(jsContent["id"]) == _targetLayerID)
                        {
                            //injecting time component for the layer
                            AddTimeInfo(jsContent, true);

                            //adding virtual aggregated fields
                            var jsFlds = jsContent["fields"] as object[];
                            var jsFldList = jsFlds.ToList();
                            jsFldList.AddRange(GetAggregatedFieldList(_valueFieldName, _valueFieldAliasName));
                            jsContent["fields"] = jsFldList.ToArray();
                        }
                    }
                }

                // Return the filter response
                return System.Text.Encoding.UTF8.GetBytes(sr.Serialize(jsonResObj));
            }
            catch (Exception ignore)
            {
                return null;
            }
        }


        private byte[] PostFilterRootLayers(RestRequestParameters restInput, byte[] responseBytes, string responseProperties, out string newResponseProperties)
        {
            newResponseProperties = responseProperties;

            if (!_isVisualizeAggregatedResultEnabled)
                return responseBytes;

            try
            {
                string originalResponse = System.Text.Encoding.UTF8.GetString(responseBytes);

                JavaScriptSerializer sr = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var jsonResObj = sr.DeserializeObject(originalResponse) as IDictionary<string, object>;

                // Filter for 'contents' tag
                var layersJA = jsonResObj["layers"] as object[];

                foreach (var item in layersJA)
                {
                    var jsLayer = item as IDictionary<string, object>;
                    if ((int)(jsLayer["id"]) == _targetLayerID)
                    {
                        //injecting time component for the layer
                        AddTimeInfo(jsLayer, true);

                        //adding virtual aggregated fields
                        var jsFlds = jsLayer["fields"] as object[];
                        var jsFldList = jsFlds.ToList();
                        jsFldList.AddRange(GetAggregatedFieldList(_valueFieldName, _valueFieldAliasName));
                        jsLayer["fields"] = jsFldList.ToArray();
                    }
                }
                // Return the filter response
                return System.Text.Encoding.UTF8.GetBytes(sr.Serialize(jsonResObj));
            }
            catch (Exception ignore)
            {
                return null;
            }
        }

        private byte[] PostFilterLayerRoot(RestRequestParameters restInput, byte[] responseBytes, string responseProperties, out string newResponseProperties)
        {
            newResponseProperties = responseProperties;

            if (!_isVisualizeAggregatedResultEnabled)
                return responseBytes;

            //append virtual aggregated fields only for the target layer
            if (!restInput.ResourceName.Equals("/layers/" + _targetLayerID, StringComparison.CurrentCultureIgnoreCase))
                return responseBytes;
            
            try
            {
                string originalResponse = System.Text.Encoding.UTF8.GetString(responseBytes);

                JavaScriptSerializer sr = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var jsLayer = sr.DeserializeObject(originalResponse) as IDictionary<string, object>;

                //appending time component for the layer
                AddTimeInfo(jsLayer, true);

                //appending virtual aggregated fields
                var jsFlds = jsLayer["fields"] as object[];
                var jsFldList = jsFlds.ToList();
                jsFldList.AddRange(GetAggregatedFieldList(_valueFieldName, _valueFieldAliasName));
                jsLayer["fields"] = jsFldList.ToArray();

                // Return the filter response
                return System.Text.Encoding.UTF8.GetBytes(sr.Serialize(jsLayer));
            }
            catch (Exception ignore)
            {
                return null;
            }
        }
        #endregion


        #region Utility code
        public String FromUnixTime(long unixTime)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddMilliseconds(unixTime).ToString("yyyy/MM/dd");
        }

        private string GetAggregateFunctionFromGenerateRenderer(IDictionary<string, object> operationInputJson)
        {
            IDictionary<string, object> cbDefJO = operationInputJson["classificationDef"] as IDictionary<string, object>;
            string type = (string)cbDefJO["type"];
            if (type.Equals("classBreaksDef", StringComparison.CurrentCultureIgnoreCase))
            {
                if (!cbDefJO.ContainsKey("classificationField"))
                    return null;

                string f = cbDefJO["classificationField"].ToString();
                if (_virtualAggregatedFields.Exists(vf => f.Equals(vf, StringComparison.CurrentCultureIgnoreCase)))
                    return f.Split('_')[0];
                else
                    return null;
            }
            else
            {
                return null;
            }
        }

        private string GetAggregateFunctionFromDrawingInfo(IDictionary<string, object> layerJson)
        {
            if (!layerJson.ContainsKey("drawingInfo"))
                return null;

            IDictionary<string, object> dwgInfoJson = layerJson["drawingInfo"] as IDictionary<string, object>;
            if (!dwgInfoJson.ContainsKey("renderer"))
                return null;

            IDictionary<string, object> rendererJson = dwgInfoJson["renderer"] as IDictionary<string, object>;
            string type = (string)rendererJson["type"];
            if (!type.Equals("classBreaks", StringComparison.CurrentCultureIgnoreCase))
                return null;

            string f = rendererJson["field"].ToString();
            if (_virtualAggregatedFields.Exists(vf => f.Equals(vf, StringComparison.CurrentCultureIgnoreCase)))
                return f.Split('_')[0];
            else
                return null;
        }

        private List<object> GetAggregatedFieldList(string valueFieldName, string valueFieldAlias)
        {
            JavaScriptSerializer sr = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            string s;
            List<object> flds = new List<object>();

            foreach (var f in _aggregatedFunctions)
            {
                string fa = f; //alias is same as function name by default
                if (_aggregationAliasDict.ContainsKey(f))
                    fa = _aggregationAliasDict[f];

                s = string.Format(_aggrVirtualFieldTemplate, f, valueFieldName, fa, valueFieldAlias);
                flds.Add(sr.DeserializeObject(s));
            }

            return flds;
        }

        private void AddTimeInfo(IDictionary<string, object> contentsJO, bool isLayer = false)
        {
            JavaScriptSerializer sr = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            string timeInfo = "";

            if (isLayer)
                timeInfo = _layerTimeInfoJSONasString;
            else
                timeInfo = _serviceTimeInfoJSONasString;

            IDictionary<string, object> ti = sr.DeserializeObject(timeInfo) as IDictionary<string, object>;
            contentsJO.Add("timeInfo", ti);
        }

        private IDictionary<string, object> FindDynamicLayerByID(object[] jsDynLyrArray, long targetLayerID)
        {
            IDictionary<string, object> jsTargetDL = jsDynLyrArray.FirstOrDefault(dl =>
            {
                var jsDL = dl as IDictionary<string, object>;
                var jsSrc = jsDL["source"] as IDictionary<string, object>;
                if (!jsSrc["type"].ToString().Equals("mapLayer", StringComparison.CurrentCultureIgnoreCase))
                    return false;

                return ((int)jsSrc["mapLayerId"] == targetLayerID);
            }) as IDictionary<string, object>;
            return jsTargetDL;
        }

        private bool GetTimeRange(IDictionary<string, object> operationInputJson, ref string stime, ref string etime)
        {
            if (operationInputJson.ContainsKey("time"))
            {
                string[] ts = operationInputJson["time"].ToString().Split(',');
                stime = ts[0];
                etime = ts[1];
                bool isTimeAvailable = (!stime.Equals("null", StringComparison.OrdinalIgnoreCase) && !etime.Equals("null", StringComparison.OrdinalIgnoreCase));
                if (isTimeAvailable)
                {
                    stime = FromUnixTime(long.Parse(stime));
                    etime = FromUnixTime(long.Parse(etime));
                    return true;
                }
                return false;
            }
            return false;
        }

        private JsonObject ReadConfigurationFile(String fileName)
        {
            try
            {
                if (!File.Exists(fileName))
                    throw new Exception("Configuration file does not exist.");

                String jsonStr = File.ReadAllText(fileName);

                var json = new JsonObject(jsonStr);
                System.Object[] aggregatedLayers = null;
                // read the aggregatedLayers array
                if (!json.TryGetArray("aggregatedLayers", out aggregatedLayers))
                    throw new Exception("'aggregatedLayers' element is missing from the configuration file.");

                foreach (var aggregatedObj in aggregatedLayers)
                {
                    JsonObject aggrJO = aggregatedObj as JsonObject;
                    if (null == aggrJO) return null;

                    // get the fqsn or service name
                    String fqsn = string.Empty;
                    if (!aggrJO.TryGetString("fqsn", out fqsn))
                        throw new Exception("'fqsn' element is missing from the configuration file.");

                    if (fqsn.Equals(string.Format("{0}.{1}", _serverObject.ConfigurationName, _serverObject.TypeName), StringComparison.CurrentCultureIgnoreCase))
                    {
                        JsonObject aggrLyrInfo = null;
                        if (aggrJO.TryGetJsonObject("aggregatedLayerInfo", out aggrLyrInfo))
                            return aggrLyrInfo;
                        else
                            throw new Exception("'aggregatedLayerInfo' element is missing from the configuration file."); ;
                    }

                    throw new Exception("No entry found for this service in the configuration file.");
                }
            }
            catch (Exception e)
            {
                throw e;
            }
            return null;
        }

        private bool InitSetup(JsonObject json)
        {
            AggregatedLayerInfo aggrLyrInfo = null;
            try
            {
                aggrLyrInfo = new AggregatedLayerInfo(json);
            }
            catch (Exception)
            {
                return false;
            }

            //generating all templates - sql query, dynamic layer and
            //setting few variables needed to generate dynamic layer with query layer
            GenerateQueryTemplates(aggrLyrInfo);

            return true;
        }

        private void GenerateQueryTemplates(AggregatedLayerInfo aggrLyrInfo)
        {
            AssetTableInfo ati = aggrLyrInfo.AssetTableInfo;
            ObservationTableInfo oti = aggrLyrInfo.ObservationTableInfo;

            _targetLayerID = aggrLyrInfo.TargetMapLayerID;
            _aggregatedFunctions = aggrLyrInfo.AggregatedFunctions;

            _valueFieldName = oti.ObservationFieldName;
            _valueFieldAliasName = oti.ObservationFieldAlias;

            StringBuilder sb = new StringBuilder();
            string assetTableAlias = "a";
            string innerQueryAlias = "o";

            string[] fldsArray = ati.OtherDescriptiveFields.Split(',');
            string descFldsQualified = string.Empty;
            string descFlds = string.Empty;
            foreach (var f in fldsArray)
            {
                descFldsQualified += string.Format("{0}.{1}, ", assetTableAlias, f.Trim());
                descFlds += string.Format("{0}, ", f.Trim());
            }
            descFldsQualified = descFldsQualified.Substring(0, descFldsQualified.Length - 2); //removing the last comma
            descFlds = descFlds.Substring(0, descFlds.Length - 2); //removing the last comma

            string allAggrFldsWithConstValue = string.Empty;
            string allAggrFlds = string.Empty;
            string allAggrFldsWithFunction = string.Empty;
            string vf;
            foreach (var s in aggrLyrInfo.AggregatedFunctions)
            {
                allAggrFldsWithConstValue += string.Format("1.0 AS {0}_{1}, ", s, oti.ObservationFieldName);
                vf = string.Format("{0}_{1}", s, oti.ObservationFieldName);
                _virtualAggregatedFields.Add(vf);
                allAggrFlds += vf + ", ";
                allAggrFldsWithFunction += string.Format("{0}({1}) AS {0}_{1}, ", s, oti.ObservationFieldName);
            }
            //removing extra comma and space at the end of the string
            allAggrFldsWithConstValue = allAggrFldsWithConstValue.Substring(0, allAggrFldsWithConstValue.Length - 2);
            allAggrFlds = allAggrFlds.Substring(0, allAggrFlds.Length - 2);
            allAggrFldsWithFunction = allAggrFldsWithFunction.Substring(0, allAggrFldsWithFunction.Length - 2);


            string sqlTimeWhereClause = string.Format("WHERE {0} BETWEEN '{{0}}' AND '{{1}}'", oti.TimeInfo.TimeFieldName);  //{0} = field-with-observation-date-time
            string sqlInnerQueryTemplate = "SELECT {0}, {{2}}({{3}}) AS {{2}}_{{3}} FROM {1} {2} GROUP BY {0}";     //{0} = asset-field-name; {1} = table name; {2} = where-clause with time
            string sqlInnerQueryTemplateWithTime = string.Format(sqlInnerQueryTemplate, 
                                                         oti.AssetIDFieldname, oti.TableName, sqlTimeWhereClause, oti.TimeInfo.TimeFieldName);
            string sqlInnerQueryTemplateWOTime = string.Format(sqlInnerQueryTemplate,
                                                       oti.AssetIDFieldname, oti.TableName, "", oti.TimeInfo.TimeFieldName);
           
            _SQLOneAggregationTemplateWithTime = string.Format("SELECT {0}.{1}, {0}.{2}, {0}.{3}, {{2}}_{{3}}, {4} FROM {5} " +
                                                 "AS {0} INNER JOIN ({6}) AS {7} ON {0}.{2} = {7}.{8}",
                                                  assetTableAlias,      //{0}
                                                  ati.OIDFieldName,     //{1}
                                                  ati.AssetIDFieldName, //{2}
                                                  ati.ShapeFieldName,   //{3}
                                                  descFldsQualified,    //{4}
                                                  ati.TableName,        //{5}
                                                  sqlInnerQueryTemplateWithTime,//{6}
                                                  innerQueryAlias,      //{7}
                                                  oti.AssetIDFieldname);//{8}
            //_SQLOneAggregationTemplateWithTime contains {0} = aggregated-function-name; {1} = value-field-name; {2} = start-time; {3} = end-time;


            _SQLOneAggregationTemplateWOTime = string.Format("SELECT {0}.{1}, {0}.{2}, {0}.{3}, {{2}}_{{3}}, {4} FROM {5} " +
                                               "AS {0} INNER JOIN ({6}) AS {7} ON {0}.{2} = {7}.{8}",
                                                assetTableAlias,      //{0}
                                                ati.OIDFieldName,     //{1}
                                                ati.AssetIDFieldName, //{2}
                                                ati.ShapeFieldName,   //{3}
                                                descFldsQualified,    //{4}
                                                ati.TableName,        //{5}
                                                sqlInnerQueryTemplateWOTime,  //{6}
                                                innerQueryAlias,      //{7}
                                                oti.AssetIDFieldname);//{8}
            //_SQLOneAggregationTemplateWOTime contains {2} = aggregated-function-name; {3} = value-field-name;

            _SQLBareBone = string.Format("SELECT {0}, {1}, {2}, {3}, {4} FROM {5}",
                                                  ati.OIDFieldName,     //{0}
                                                  ati.AssetIDFieldName, //{1}
                                                  ati.ShapeFieldName,   //{2}
                                                  allAggrFldsWithConstValue,//{3} 
                                                  descFlds,             //{4}
                                                  ati.TableName);       //{5}

            string SQLAllAggregationTemplate = "SELECT {0}.{1}, {0}.{2}, {0}.{3}, {4}, {5} FROM {6} " +
                                               "AS {0} INNER JOIN (SELECT {7}, {8} FROM {9} {10} GROUP BY {7}) " +
                                               "AS {11} " +
                                               "ON {0}.{2} = {11}.{7}";
            _SQLAllAggregationsWOTime = string.Format(SQLAllAggregationTemplate,
                                                      assetTableAlias,      //{0}
                                                      ati.OIDFieldName,     //{1}
                                                      ati.AssetIDFieldName, //{2}
                                                      ati.ShapeFieldName,   //{3}
                                                      allAggrFlds,          //{4}
                                                      descFldsQualified,             //{5}
                                                      ati.TableName,        //{6}
                                                      oti.AssetIDFieldname,  //{7}
                                                      allAggrFldsWithFunction,  //{8}
                                                      oti.TableName,        //{9}
                                                      "",                   //{10} leaving where-clause blank
                                                      innerQueryAlias);     //{11}
            
            _SQLAllAggregationsTemplateWithTime = string.Format(SQLAllAggregationTemplate,
                                                                assetTableAlias,      //{0}
                                                                ati.OIDFieldName,     //{1}
                                                                ati.AssetIDFieldName, //{2}
                                                                ati.ShapeFieldName,   //{3}
                                                                allAggrFlds,          //{4}
                                                                descFldsQualified,             //{5}
                                                                ati.TableName,        //{6}
                                                                oti.AssetIDFieldname,  //{7}
                                                                allAggrFldsWithFunction,  //{8}
                                                                oti.TableName,        //{9}
                                                                sqlTimeWhereClause,   //{10}
                                                                innerQueryAlias);     //{11}
            //_SQLAllAggregationsTemplateWithTime contains {0} = start-time; {1} = end-time; 
            
            _defaultDynamicLayerJSONasString = string.Format("{{ \"id\": 101, \"source\": {{ \"type\": \"dataLayer\", \"dataSource\": {{ \"type\": \"queryTable\", \"workspaceId\": \"{0}\", \"query\": \"\", \"oidFields\": \"{1}\", \"geometryType\": \"{2}\", \"spatialReference\": {3} }} }}, \"drawingInfo\": {{ }} }}",
                                                              aggrLyrInfo.DynamicWorkspaceID,
                                                              ati.OIDFieldName,
                                                              ati.GeometryType,
                                                              ati.WKID);

            _serviceTimeInfoJSONasString = string.Format("{{\"timeExtent\":[{0},{1}],\"timeReference\":null,\"timeRelation\":\"esriTimeRelationOverlaps\",\"defaultTimeInterval\":{2},\"defaultTimeIntervalUnits\":\"{3}\",\"defaultTimeWindow\":{4},\"hasLiveData\":false}}", 
                                                          oti.TimeInfo.StartTime,               //{0}
                                                          oti.TimeInfo.EndTime,                 //{1}
                                                          oti.TimeInfo.DefaultTimeInterval,     //{2}
                                                          oti.TimeInfo.DefaultTimeIntervalUnits,//{3}
                                                          oti.TimeInfo.DefaultTimeWindow);      //{4}
                                                          
            _layerTimeInfoJSONasString = string.Format("{{\"startTimeField\": \"{0}\", \"timeExtent\":[{1},{2}],\"timeReference\":null,\"timeInterval\":{3},\"timeIntervalUnits\":\"{4}\",\"hasLiveData\":false,\"exportOptions\": {{\"useTime\": true, \"timeDataCumulative\": false, \"timeOffset\": null, \"timeOffsetUnits\": null }} }}",
                                                        oti.TimeInfo.TimeFieldName,             //{0}                       
                                                        oti.TimeInfo.StartTime,                 //{1}
                                                        oti.TimeInfo.EndTime,                   //{2}
                                                        oti.TimeInfo.DefaultTimeInterval,       //{3}
                                                        oti.TimeInfo.DefaultTimeIntervalUnits); //{4}
                                                        
        }
        
        #endregion
    }

    #region Utility classes

    class AssetTableInfo
    {
        public readonly string TableName;
        public readonly string OIDFieldName;
        public readonly string ShapeFieldName;
        public readonly string AssetIDFieldName;
        public readonly string OtherDescriptiveFields;
        public readonly long WKID;
        public readonly string GeometryType;
        public AssetTableInfo(JsonObject assetTblJO)
        {
            string value = string.Empty;
            long? valLong = null;

            if (!assetTblJO.TryGetString("TableName", out value))
                throw new Exception("TableName property missing from the JSON");
            TableName = value;

            if (!assetTblJO.TryGetString("OIDFieldName", out value))
                throw new Exception("OIDFieldName property missing from the JSON");
            OIDFieldName = value;

            if (!assetTblJO.TryGetString("ShapeFieldName", out value))
                throw new Exception("ShapeFieldName property missing from the JSON");
            ShapeFieldName = value;

            if (!assetTblJO.TryGetString("AssetIDFieldName", out value))
                throw new Exception("AssetIDFieldName property missing from the JSON");
            AssetIDFieldName = value;

            if (!assetTblJO.TryGetString("OtherDescriptiveFields", out value))
                throw new Exception("OtherDescriptiveFields property missing from the JSON");
            OtherDescriptiveFields = value;

            if (!assetTblJO.TryGetAsLong("WKID", out valLong))
                throw new Exception("WKID property missing from the JSON");
            WKID = valLong.Value;

            if (!assetTblJO.TryGetString("GeometryType", out value))
                throw new Exception("GeometryType property missing from the JSON");
            GeometryType = value;
        }
    }

    class ObservationTableInfo
    {
        public readonly string TableName;
        public readonly string AssetIDFieldname;
        public readonly string ObservationFieldName;
        public readonly string ObservationFieldAlias;
        public readonly TimeInfo TimeInfo;
        
        public ObservationTableInfo(JsonObject obsrvTblInfo)
        {
            string value = string.Empty;

            if (!obsrvTblInfo.TryGetString("TableName", out value))
                throw new Exception("TableName property missing from the JSON");
            TableName = value;

            if (!obsrvTblInfo.TryGetString("AssetIDFieldname", out value))
                throw new Exception("AssetIDFieldname property missing from the JSON");
            AssetIDFieldname = value;

            if (!obsrvTblInfo.TryGetString("ObservationFieldName", out value))
                throw new Exception("ObservationFieldName property missing from the JSON");
            ObservationFieldName = value;

            if (!obsrvTblInfo.TryGetString("ObservationFieldAlias", out value))
                throw new Exception("ObservationFieldAlias property missing from the JSON");
            ObservationFieldAlias = value;

            JsonObject jo = null;
            if (!obsrvTblInfo.TryGetJsonObject("TimeInfo", out jo))
                throw new Exception("TimeInfo property missing from the JSON");
            TimeInfo = new TimeInfo(jo);
        }
    }

    class TimeInfo
    {
        public readonly string TimeFieldName;
        public readonly long StartTime;
        public readonly long EndTime;
        public readonly int DefaultTimeInterval;
        public readonly string DefaultTimeIntervalUnits;
        public readonly int DefaultTimeWindow;

        public TimeInfo(JsonObject timeInfo)
        {
            string value = string.Empty;
            long? valLong = null;

            if (!timeInfo.TryGetString("TimeFieldName", out value))
                throw new Exception("TimeFieldName property missing from the JSON");
            TimeFieldName = value;

            if (!timeInfo.TryGetAsLong("StartTime", out valLong))
                throw new Exception("StartTime property missing from the JSON");
            StartTime = valLong.Value;

            if (!timeInfo.TryGetAsLong("EndTime", out valLong))
                throw new Exception("EndTime property missing from the JSON");
            EndTime = valLong.Value; 
            
            if (!timeInfo.TryGetAsLong("TimeInterval", out valLong))
                throw new Exception("TimeInterval property missing from the JSON");
            DefaultTimeInterval = (int)valLong.Value;

            if (!timeInfo.TryGetString("TimeIntervalUnits", out value))
                throw new Exception("TimeIntervalUnits property missing from the JSON");
            DefaultTimeIntervalUnits = value;

            if (!timeInfo.TryGetAsLong("TimeWindow", out valLong))
                throw new Exception("TimeWindow property missing from the JSON");
            DefaultTimeWindow = (int)valLong.Value;
        }
    }

    class AggregatedLayerInfo
    {
        public readonly string DynamicWorkspaceID;
        public readonly long TargetMapLayerID;
        public readonly string[] AggregatedFunctions;
        public readonly AssetTableInfo AssetTableInfo;
        public readonly ObservationTableInfo ObservationTableInfo;

        public AggregatedLayerInfo(JsonObject aggrtLyrInfoJO)
        {
            string value = string.Empty;
            long? valLong = null;
            JsonObject jo = null;

            if (!aggrtLyrInfoJO.TryGetString("DynamicWorkspaceID", out value))
                throw new Exception("DynamicWorkspaceID property missing from the JSON");
            DynamicWorkspaceID = value;

            if (!aggrtLyrInfoJO.TryGetAsLong("TargetMapLayerID", out valLong))
                throw new Exception("TargetMapLayerID property missing from the JSON");
            TargetMapLayerID = valLong.Value;

            //setting all aggregated functions in an array
            System.Object[] aggrFuncObj = null;
            if (aggrtLyrInfoJO.TryGetArray("AggregatedFunctions", out aggrFuncObj))
            {
                List<string> aggrFuncNames = new List<string>(aggrFuncObj.Length);
                foreach (var item in aggrFuncObj)
                    aggrFuncNames.Add(item.ToString().ToUpper());

                AggregatedFunctions = aggrFuncNames.ToArray();
            }
            else
            {
                throw new Exception("AggregatedFunctions property missing from the JSON");
            }

            if (!aggrtLyrInfoJO.TryGetJsonObject("AssetTableInfo", out jo))
                throw new Exception("AssetTableInfo property missing from the JSON");
            AssetTableInfo = new AssetTableInfo(jo);

            if (!aggrtLyrInfoJO.TryGetJsonObject("ObservationTableInfo", out jo))
                throw new Exception("ObservationTableInfo property missing from the JSON");
            ObservationTableInfo = new ObservationTableInfo(jo);
        }
    }
    #endregion
}

