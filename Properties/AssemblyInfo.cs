using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following 
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("GmailCardDAVSync")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("GmailCardDAVSync")]
[assembly: AssemblyCopyright("Copyright ©  2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version 
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers 
// by using the '*' as shown below:
// [assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: ComVisible(false)]
<!--
    This file contains Runtime Directives used by .NET Native. The defaults here are suitable for most
    developers. However, you can modify these parameters to modify the behavior of the .NET Native
    optimizer.

    Runtime Directives are documented at https://go.microsoft.com/fwlink/?LinkID=391919

    To fully enable reflection for App1.MyClass and all of its public/private members
    <Type Name="App1.MyClass" Dynamic="Required All"/>

    To enable dynamic creation of the specific instantiation of AppClass<T> over System.Int32
    <TypeInstantiation Name="App1.AppClass" Arguments="System.Int32" Activate="Required Public" />

    Using the Namespace directive to apply reflection policy to all the types in a particular namespace
    <Namespace Name="DataClasses.ViewModels" Serialize="All" />
-->

<Directives xmlns="http://schemas.microsoft.com/netfx/2013/01/metadata">
  <Application>
    <!--
      An Assembly element with Name="*Application*" applies to all assemblies in
      the application package. The asterisks are not wildcards.
    -->
    <Assembly Name="*Application*" Dynamic="Required All" />
    
    
    <!-- Add your application specific runtime directives here. -->


  </Application>
</Directives>
