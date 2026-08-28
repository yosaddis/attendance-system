using System.Runtime.CompilerServices;
using System.Windows;

// Lets AttendanceAgent.Tests exercise internal types (e.g.
// AttendanceAgent.Devices.SecuGen.SecuGenFirTextEncoding) directly, without
// requiring hardware.
[assembly: InternalsVisibleTo("AttendanceAgent.Tests")]

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
