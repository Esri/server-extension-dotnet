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

using ESRI.ArcGIS.SOESupport.SOI;

namespace SimplifyMapContentsSOI
{
    /// <summary>
    /// TODO
    /// </summary>
    class CustomRESTHandlerOpCode : RestHandlerOpCode
    {
      /// <summary>
      /// TODO
      /// </summary>
      public static readonly CustomRESTHandlerOpCode RootReturnUpdates = new CustomRESTHandlerOpCode(888);
      public static readonly CustomRESTHandlerOpCode LayerRootReturnUpdates = new CustomRESTHandlerOpCode(999);

      /// <summary>
      /// TODO
      /// </summary>
      /// <param name="internalValue"></param>
      protected CustomRESTHandlerOpCode(int internalValue) : base(internalValue) { }
    }
}
