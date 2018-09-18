/*
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
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Collections.Specialized;

using System.Runtime.InteropServices;

using ESRI.ArcGIS.esriSystem;
using ESRI.ArcGIS.Server;
using ESRI.ArcGIS.Geometry;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.SOESupport;
using ESRI.ArcGIS.SOESupport.SOI;
using System.Web.Script.Serialization;
using System.IO;


namespace NWC
{
  [ComVisible(true)]
  [Guid("117d4dec-5cc0-471f-b897-6779166eda2b")]
  [ClassInterface(ClassInterfaceType.None)]
  [ServerObjectInterceptor("MapServer",
      Description = "",
      DisplayName = "NWC",
      Properties = "")]
  public class NWC : IServerObjectExtension, IRESTRequestHandler
  {
    private string _soiName;
    private IServerObjectHelper _soHelper;
    private ServerLogger _serverLog;
    private RestSOIHelper _restServiceSOI;
    private String _configFilePath = @"C:\arcgisserver\NWC.json";
    private String _timeFieldName = ""; // = "TimeValue";
    private String _liveTimeLayerID = "";
    private String _queryableLayerID = "";
    private object[] _customLegend = null;
    private String _defaultOrderBy = "";
    private String _defaultResultRecordCount = "";
    private int _defaultTimeInterval = -1;
    private esriTimeUnits _defaultTimeIntervalUnits = esriTimeUnits.esriTimeUnitsUnknown;

    public NWC()
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
                { MyRESTHandlerOpCode.RootReturnUpdates, new RestFilter 
                                            { 
                                                PreFilter = PreRootReturnUpdates, 
                                                PostFilter = PostRootReturnUpdates 
                                            } },
                { RestHandlerOpCode.LayerQuery, new RestFilter
                                            { 
                                                PreFilter = PreFilterLayerQuery, 
                                                PostFilter = null  
                                            } }
            };
    }
    
    public void Init(IServerObjectHelper pSOH)
    {
      System.Diagnostics.Debugger.Launch(); 
      
      _soHelper = pSOH;
      _serverLog = new ServerLogger();
      _restServiceSOI = new RestSOIHelper(pSOH);
      ReadConfigFile(pSOH);
      InitFiltering();
      _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".init()", 200, "Initialized " + _soiName + " SOI.");
    }

    public void Shutdown()
    {
      _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".init()", 200, "Shutting down " + _soiName + " SOI.");
    }

    #region REST interceptors

    public string GetSchema()
    {
      IRESTRequestHandler restRequestHandler = _restServiceSOI.FindRequestHandlerDelegate<IRESTRequestHandler>();
      if (restRequestHandler == null)
        return null;

      return restRequestHandler.GetSchema();
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

    private RestHandlerOpCode GetHandlerOpCode(string resourceName, string operationName, bool isAskingForReturnUpdates = false)
    {
      if (isAskingForReturnUpdates)
        return MyRESTHandlerOpCode.RootReturnUpdates;


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
                default:
                  return RestHandlerOpCode.DefaultNoOp;
              }
          }
          break;
        case "legend":
          return RestHandlerOpCode.RootLegend;
        case "dynamiclayer":
          switch (opName)
          {
            case "query":
              return RestHandlerOpCode.DefaultNoOp; //MyRESTHandlerOpCode.DynamicLayerQuery;
            default:
              return RestHandlerOpCode.DefaultNoOp;
          }
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

        //checking for 'returnUpdates at the root resources
        bool isAskingForReturnUpdates = false;
        if (restInput.OperationName == "") //((restInput.ResourceName == "") && (restInput.OperationName == ""))
        {
          JavaScriptSerializer js = new JavaScriptSerializer() { MaxJsonLength = int.MaxValue };
          var inputJson = js.DeserializeObject(restInput.OperationInput) as IDictionary<string, object>;
          isAskingForReturnUpdates = inputJson.ContainsKey("returnUpdates");
          js = null;
        } 
        
        var opCode = GetHandlerOpCode(restInput.ResourceName, restInput.OperationName, isAskingForReturnUpdates);

        return FilterRESTRequest(opCode, restInput, out responseProperties);
      }
      catch (Exception e)
      {
        _serverLog.LogMessage(ServerLogger.msgType.error, _soiName + ".HandleRESTRequest()", 500, "Exception: " + e.Message + " in " + e.StackTrace);
        throw;
      }
    }

    #endregion

    #region Pre-filters
    private RestRequestParameters PreRootReturnUpdates(RestRequestParameters restInput)
    {
      //redirecting the call to one of the layer's query operation
      //to compute full time extent very quickly
      restInput.ResourceName = "layers/" + _liveTimeLayerID; //_queryableLayerID;
      restInput.OperationName = "query";
      
      //hardcoded json string
      restInput.OperationInput =
        string.Format("{{\"outStatistics\": [{{\"statisticType\": \"min\", \"onStatisticField\": \"{0}\"}}, {{\"statisticType\": \"max\", \"onStatisticField\": \"{0}\" }}]}}", _timeFieldName);

      return restInput;
    }

    private RestRequestParameters PreFilterExport(RestRequestParameters restInput)
    {
      JavaScriptSerializer sr = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
      var operationInputJson = sr.DeserializeObject(restInput.OperationInput) as IDictionary<string, object>;

      //Bug: AGOL time-slider does not support time-stamp; it is a time-range always
      //while for map server, time-range is exlusive from both end; as a result the request goes to the
      //underline database looks like timeField >= startTime and timeField <= endTime
      //that makes it to return features for 2 time-steps.
      //also, on my database, i noticed that executing a query like "timeField = t1" is faster than "timeField >= t1 and timeField <= t2"
      //Workaround: removing the endTime from the time value.
      string timeParam = "time";
      if (operationInputJson.ContainsKey(timeParam))
      {
        //string timeValues = operationInputJson[timeParam].ToString();
        //operationInputJson[timeParam] = timeValues.Split(',')[0];
        string timeValues = operationInputJson[timeParam].ToString();
        string[] timeValueArray = timeValues.Split(',');
        //check whether the values are null,null
        if (timeValueArray[0].Equals("null", StringComparison.CurrentCultureIgnoreCase) &&
            timeValueArray[0].Equals("null", StringComparison.CurrentCultureIgnoreCase))
        {
          operationInputJson[timeParam] = GetDefaultTimeWhenMissing();
        }
        else
        {
          operationInputJson[timeParam] = timeValueArray[0];
        }
      }
      else //when time is not passed in, use the time instant closest to NOW and draw features only for that time instant
      {
        operationInputJson.Add(timeParam, GetDefaultTimeWhenMissing());
      }

      if (operationInputJson.ContainsKey("layers"))
        operationInputJson["layers"] = "";

      restInput.OperationInput = sr.Serialize(operationInputJson);
      return restInput;
    }

    private RestRequestParameters PreFilterLayerQuery(RestRequestParameters restInput)
    {
      //redirecting all query calls to the one specific layer
      //and limiting to return only 1 stream with hightest streamOrder by default
      restInput.ResourceName = "layers/" + _queryableLayerID;

      JavaScriptSerializer sr = new JavaScriptSerializer() { MaxJsonLength = int.MaxValue };
      var jsonInputObj = sr.DeserializeObject(restInput.OperationInput) as IDictionary<string, object>;

      string timeParam = "time";
      if (jsonInputObj.ContainsKey(timeParam))
      {
        //string timeValues = jsonInputObj[timeParam].ToString();
        //jsonInputObj[timeParam] = timeValues.Split(',')[0];
        string timeValues = jsonInputObj[timeParam].ToString();
        string[] timeValueArray = timeValues.Split(',');
        //check whether the values are null,null
        if (timeValueArray[0].Equals("null", StringComparison.CurrentCultureIgnoreCase) &&
            timeValueArray[0].Equals("null", StringComparison.CurrentCultureIgnoreCase))
        {
          jsonInputObj[timeParam] = GetDefaultTimeWhenMissing();
        }
        else
        {
          jsonInputObj[timeParam] = timeValueArray[0];
        }
      }
      else //when time is not passed in, use the time instant closest to NOW and draw features only for that time instant
      {
        jsonInputObj.Add(timeParam, GetDefaultTimeWhenMissing());
      }

      //it is not meant to override user's inputs for OrderBy and ResultRecordCount
      //only adding defaults when user does not specify those parameters
      string orderByParam = "orderByFields";
      if (jsonInputObj.ContainsKey(orderByParam))
      {
        if (jsonInputObj[orderByParam].ToString().Trim() == "")
        {
          jsonInputObj[orderByParam] = _defaultOrderBy;
        }
      }
      else
      {
        jsonInputObj.Add(orderByParam, _defaultOrderBy);
      }

      string resultRecCountParam = "resultRecordCount";
      if (jsonInputObj.ContainsKey(resultRecCountParam))
      {
        if (jsonInputObj[resultRecCountParam].ToString().Trim() == "")
        {
          jsonInputObj[resultRecCountParam] = _defaultResultRecordCount;
        }
      }
      else
      {
        jsonInputObj.Add(resultRecCountParam, _defaultResultRecordCount);
      }

      //Note: since as of today (5/25/2016) field list from layer (where features are combined and generalized) does not match with
      // the bottm layer with detail streams, i'm always setting outFields to '*'
      // it's a temporay workaround
      if (jsonInputObj.ContainsKey("outFields"))
        jsonInputObj["outFields"] = "*";

      restInput.OperationInput =  sr.Serialize(jsonInputObj);
      sr = null;
      return restInput;
    }
    #endregion

    #region REST Post-filters
    private byte[] PostFilterRESTRoot(RestRequestParameters restInput, byte[] responseBytes, string responseProperties, out string newResponseProperties)
    {
      newResponseProperties = responseProperties;

      string originalResponse = System.Text.Encoding.UTF8.GetString(responseBytes);
      JavaScriptSerializer sr = new JavaScriptSerializer() { MaxJsonLength = int.MaxValue };
      var jsonResObj = sr.DeserializeObject(originalResponse) as IDictionary<string, object>;

      var contentsJO = jsonResObj["contents"] as IDictionary<string, object>;

      //modifying light version of 'Layers' resources
      var layersJA = contentsJO["layers"] as object[];
      //var jsLightLayer = layersJA[0] as IDictionary<string, object>;
      var jsLightLayer = layersJA.FirstOrDefault(j =>
      {
        var d = j as Dictionary<string, object>;
        return _queryableLayerID.Equals(d["id"].ToString());
      }) as IDictionary<string, object>; 
      jsLightLayer["minScale"] = 0;
      jsLightLayer["maxScale"] = 0;
      contentsJO["layers"] = new object[] { jsLightLayer }; //only returning the very first layer

      //modifying All Layer Resources
      var allResourcesJA = jsonResObj["resources"] as object[];
      var layersRJO = allResourcesJA.FirstOrDefault(e =>
      {
        var jo = e as IDictionary<string, object>;
        if (!jo.ContainsKey("name"))
          return false;
        var name = jo["name"].ToString();
        return ("layers" == name);
      }) as IDictionary<string, object>;

      var layerResourceJA = layersRJO["resources"] as object[];
      //we need to return only the first layer and remove visibility scale range from there.
      //var jsDetailLayer = layerResourceJA[0] as IDictionary<string, object>;

      //returning only the layer with detail streams i.e. the layerId = _querableLayerID
      var jsDetailLayer = layerResourceJA.FirstOrDefault(j =>
        {
          var d = j as Dictionary<string, object>;
          var jsContent = d["contents"] as IDictionary<string, object>;
          return _queryableLayerID.Equals(jsContent["id"].ToString(), StringComparison.CurrentCultureIgnoreCase);
        }) as IDictionary<string, object>;

      var jsDetailLyrContent = jsDetailLayer["contents"] as IDictionary<string, object>;
      jsDetailLyrContent["minScale"] = 0;
      jsDetailLyrContent["maxScale"] = 0;
      layersRJO["resources"] = new object[] { jsDetailLayer };

      //updating the queryableLayer's and root's timeExtent with the timeExtent from the very first layer
      //this is due to a bug in map server that makes the service to take really long time to compute a layer (when 1:M joined to a pretty large table) time's extent
      var jsDetailLayer0 = layerResourceJA[0] as IDictionary<string, object>; //the first layer
      var jsDetailLyrContent0 = jsDetailLayer0["contents"] as IDictionary<string, object>;
      var jsTimeInfoLyr0 = jsDetailLyrContent0["timeInfo"] as IDictionary<string, object>; //the timeInfor from the very first layer
      //var jsTimeExtentLyr0 = jsTimeInfoLyr0["timeExtent"] as object[];
      var jsDetailLyrTimeInfo = jsDetailLyrContent["timeInfo"] as IDictionary<string, object>;
      jsDetailLyrTimeInfo["timeExtent"] = jsTimeInfoLyr0["timeExtent"];
      //var jsDetailLyrTimeExtent = jsDetailLyrTimeInfo["timeExtent"] as object[];
      //jsDetailLyrTimeExtent[0] = jsTimeExtentLyr0[0];
      //jsDetailLyrTimeExtent[1] = jsTimeExtentLyr0[1];
      //update time extent for the root resources
      var jsTimeInfoRoot = contentsJO["timeInfo"] as IDictionary<string, object>;
      jsTimeInfoRoot["timeExtent"] = jsTimeInfoLyr0["timeExtent"];
      _defaultTimeInterval = (int)jsTimeInfoRoot["defaultTimeInterval"];
      _defaultTimeIntervalUnits = (esriTimeUnits)Enum.Parse(typeof(esriTimeUnits), (string)jsTimeInfoRoot["defaultTimeIntervalUnits"]);

      //var jsTimeExtentRoot = jsTimeInfoRoot["timeExtent"] as object[];
      //jsTimeExtentRoot[0] = jsTimeExtentLyr0[0];
      //jsTimeExtentRoot[1] = jsTimeExtentLyr0[1];

      //modifying legends
      var legendRJO = allResourcesJA.FirstOrDefault(e =>
      {
        var jo = e as IDictionary<string, object>;
        if (!jo.ContainsKey("name"))
          return false;
        var name = jo["name"].ToString();
        return ("legend" == name);
      }) as IDictionary<string, object>;
      var jsLegendLyrContent = legendRJO["contents"] as IDictionary<string, object>;
      var jsLgdLayers = jsLegendLyrContent["layers"] as object[];
      //var jsLgdLyr = jsLgdLayers[0] as IDictionary<string, object>;
      var jsLgdLyr = jsLgdLayers.FirstOrDefault(j =>
      {
        var d = j as Dictionary<string, object>;
        return _queryableLayerID.Equals(d["layerId"].ToString(), StringComparison.CurrentCultureIgnoreCase);
      }) as IDictionary<string, object>;

      jsLgdLyr["minScale"] = 0;
      jsLgdLyr["maxScale"] = 0;
      if (_customLegend != null)
        jsLgdLyr["legend"] = _customLegend; //GetCustomLegend();
      jsLegendLyrContent["layers"] = new object[] { jsLgdLyr };

      return System.Text.Encoding.UTF8.GetBytes(sr.Serialize(jsonResObj));
    }

    private byte[] PostRootReturnUpdates(RestRequestParameters restInput, byte[] responseBytes, string responseProperties, out string newResponseProperties)
    {
      newResponseProperties = responseProperties;
      if (responseBytes == null)
        return Encoding.UTF8.GetBytes("{}"); 

      //reformatting the query response to comform that to the json specs when MapServer?returnUpdate=true
      IDictionary<string, object> output = new Dictionary<string, object>();
      output.Add("timeExtent", null);

      string originalResponse = System.Text.Encoding.UTF8.GetString(responseBytes);
      JavaScriptSerializer sr = new JavaScriptSerializer() { MaxJsonLength = int.MaxValue };
      var jsonResObj = sr.DeserializeObject(originalResponse) as IDictionary<string, object>;
      var jsonFeatures = jsonResObj["features"] as object[];
      var jsonFeat = jsonFeatures[0] as IDictionary<string, object>;
      var jsonAttr = jsonFeat["attributes"] as IDictionary<string, object>;
      var timeExtArray = new Int64[2];
      timeExtArray[0] = Convert.ToInt64(jsonAttr["MIN_" + _timeFieldName]);
      timeExtArray[1] = Convert.ToInt64(jsonAttr["MAX_" + _timeFieldName]);
      output["timeExtent"] = timeExtArray;

      string outputJsonStr = sr.Serialize(output);
      sr = null;
      return System.Text.Encoding.UTF8.GetBytes(outputJsonStr);
    }
    #endregion

    #region Utility methods
    private void ReadConfigFile(IServerObjectHelper pSOH)
    {
      if (!File.Exists(_configFilePath))
        throw new Exception("The json file defining scale dependent definition queries is not found.");

      String jsonStr = File.ReadAllText(_configFilePath);
      JavaScriptSerializer sr = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
      var jsonConfigArray = sr.DeserializeObject(jsonStr) as object[];

      var n = string.Format("{0}.{1}", pSOH.ServerObject.ConfigurationName, pSOH.ServerObject.TypeName);
      _serverLog.LogMessage(ServerLogger.msgType.infoStandard, "", 999, "SO Name: " + n);
      foreach (var item in jsonConfigArray)
      {
        var d = item as Dictionary<string, object>;
        var fqsnJSName = d["fqsn"].ToString();
        _serverLog.LogMessage(ServerLogger.msgType.infoStandard, "", 999, "FQSN from JSON: " + fqsnJSName);
        _serverLog.LogMessage(ServerLogger.msgType.infoStandard, "", 999, string.Format("{0} == {1} : Matched? {2}", n, fqsnJSName, n.Equals(fqsnJSName, StringComparison.CurrentCultureIgnoreCase)));
      }


      var jsonConfig = jsonConfigArray.FirstOrDefault(j =>
      {
        var d = j as Dictionary<string, object>;
        var soFullName = string.Format("{0}.{1}", pSOH.ServerObject.ConfigurationName, pSOH.ServerObject.TypeName);
        if (d["fqsn"].ToString().Equals(soFullName, StringComparison.CurrentCultureIgnoreCase))
          return true;
        else
          return false;
      }) as Dictionary<string, object>; 


      _queryableLayerID = jsonConfig["redirectQueryToLayerID"].ToString();
      _timeFieldName = jsonConfig["timeFieldName"].ToString();
      _liveTimeLayerID = jsonConfig["liveTimeLayerID"].ToString();
      _defaultOrderBy = jsonConfig["defaultOrderByFields"].ToString();
      _defaultResultRecordCount = jsonConfig["defaultResultRecordCount"].ToString();
      if (jsonConfig.ContainsKey("legend"))
        _customLegend = jsonConfig["legend"] as object[];
      sr = null;
    }

    /// <summary>
    /// Returns a time window that between {NOW - half of default interval} and {NOW + half of default interval}
    /// </summary>
    /// <returns>string in '{min_epoch}, {max_epoch}' format</returns>
    private string GetDefaultTimeWhenMissing()
    {
      DateTime defaultIntervalDateTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
      //adding the default date interval to the epochBaseDateTime
      //basically to convert the default date interval to epoch value
      switch (_defaultTimeIntervalUnits)
      {
        case esriTimeUnits.esriTimeUnitsMilliseconds:
          defaultIntervalDateTime = defaultIntervalDateTime.AddMilliseconds(_defaultTimeInterval);
          break;
        case esriTimeUnits.esriTimeUnitsSeconds:
          defaultIntervalDateTime = defaultIntervalDateTime.AddSeconds(_defaultTimeInterval);
          break;
        case esriTimeUnits.esriTimeUnitsMinutes:
          defaultIntervalDateTime = defaultIntervalDateTime.AddMinutes(_defaultTimeInterval);
          break;
        case esriTimeUnits.esriTimeUnitsHours:
          defaultIntervalDateTime = defaultIntervalDateTime.AddHours(_defaultTimeInterval);
          break;
        case esriTimeUnits.esriTimeUnitsDays:
          defaultIntervalDateTime = defaultIntervalDateTime.AddDays(_defaultTimeInterval);
          break;
        case esriTimeUnits.esriTimeUnitsWeeks:
          defaultIntervalDateTime = defaultIntervalDateTime.AddDays(_defaultTimeInterval * 7);
          break;
        case esriTimeUnits.esriTimeUnitsMonths:
          defaultIntervalDateTime = defaultIntervalDateTime.AddMonths(_defaultTimeInterval);
          break;
        case esriTimeUnits.esriTimeUnitsYears:
          defaultIntervalDateTime = defaultIntervalDateTime.AddYears(_defaultTimeInterval);
          break;
        case esriTimeUnits.esriTimeUnitsDecades:
          defaultIntervalDateTime = defaultIntervalDateTime.AddYears(_defaultTimeInterval * 10);
          break;
        case esriTimeUnits.esriTimeUnitsCenturies:
          defaultIntervalDateTime = defaultIntervalDateTime.AddYears(_defaultTimeInterval * 100);
          break;
        case esriTimeUnits.esriTimeUnitsUnknown:
          return "";
        default:
          return "";
      }

      long epochNow = DateTime.UtcNow.ToEpochTime();
      long epochHalfInterval = defaultIntervalDateTime.ToEpochTime() / 2;
      return string.Format("{0},{1}", epochNow - epochHalfInterval, (epochNow + epochHalfInterval - 1000)); //making the end time 1 sec less since map server time query both start and end values are inclusive
    }
    #endregion
  }

  public static class ExtensionMethods
  {
    /// <summary>
    /// Converts date time (assumed in UTC) to an epoch time
    /// It assumes the input date time in UTC as well... no timezone conversion happens
    /// </summary>
    /// <param name="date">datetime in UTC</param>
    /// <returns>epoch time</returns>
    public static long ToEpochTime(this DateTime date)
    {
      var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
      return Convert.ToInt64((date - epoch).TotalMilliseconds);
    }
  }
}
