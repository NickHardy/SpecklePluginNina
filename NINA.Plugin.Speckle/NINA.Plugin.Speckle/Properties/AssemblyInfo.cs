using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// [MANDATORY] The following GUID is used as a unique identifier of the plugin. Generate a fresh one for your plugin!
[assembly: Guid("543704e0-51ac-492c-4e94-3c85f2c07e22")]

// [MANDATORY] The name of your plugin
//[assembly: AssemblyTitle("Speckle Interferometry")]
// [MANDATORY] A short description of your plugin
[assembly: AssemblyDescription("Plugin for the acquisition of interferometry data for closely separated objects.")]


// The following attributes are not required for the plugin per se, but are required by the official manifest meta data

// Your name
//[assembly: AssemblyCompany("Nick Hardy & Leon Bewersdorff")] Filled in csproj
// The product name that this plugin is part of
//[assembly: AssemblyProduct("Speckle Interferometry")] Filled in csproj
[assembly: AssemblyCopyright("")]

// The minimum Version of N.I.N.A. that this plugin is compatible with
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.3005")]

// The license your plugin code is using
[assembly: AssemblyMetadata("License", "MIT")]
// The url to the license
[assembly: AssemblyMetadata("LicenseURL", "https://opensource.org/licenses/MIT")]
// The repository where your pluggin is hosted
[assembly: AssemblyMetadata("Repository", "https://github.com/NickHardy/SpecklePluginNina")]


// The following attributes are optional for the official manifest meta data

//[Optional] Your plugin homepage URL - omit if not applicaple
[assembly: AssemblyMetadata("Homepage", "https://stelar.groups.io/")]

//[Optional] Common tags that quickly describe your plugin
[assembly: AssemblyMetadata("Tags", "Speckle,Interferometry,Acquisition")]

//[Optional] A link that will show a log of all changes in between your plugin's versions
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/NickHardy/SpecklePluginNina")]

//[Optional] The url to a featured logo that will be displayed in the plugin list next to the name
[assembly: AssemblyMetadata("FeaturedImageURL", "https://raw.githubusercontent.com/NickHardy/SpecklePluginNina/version3/NINA.Plugin.Speckle/NINA.Plugin.Speckle/Resources/SpeckleThumb.png")]
//[Optional] A url to an example screenshot of your plugin in action
[assembly: AssemblyMetadata("ScreenshotURL", "https://github.com/NickHardy/SpecklePluginNina/blob/net8version/NINA.Plugin.Speckle/NINA.Plugin.Speckle/Resources/SpeckleOrbits.png?raw=true")]
//[Optional] An additional url to an example example screenshot of your plugin in action
[assembly: AssemblyMetadata("AltScreenshotURL", "https://github.com/NickHardy/SpecklePluginNina/blob/net8version/NINA.Plugin.Speckle/NINA.Plugin.Speckle/Resources/ListSequence.png?raw=true")]
//[Optional] An in-depth description of your plugin
[assembly: AssemblyMetadata("LongDescription", @"#Speckle Interferometry data acquisition plugin#

Automated acquisition of speckle interferometry data: many very short exposures of a small region of interest, no guiding, no dithering, and moonlight does not matter. Resolves pairs below the seeing limit. Almost any telescope works; more aperture and focal length reach fainter pairs at smaller separations.

## Getting started ##

1. Add the ""Speckle"" panel to the Imaging tab.
2. Set your telescope and camera under Options > Plugins > SpeckleInterferometry.
3. Load a target list, then press Start.

The panel works through the night on its own: pick a target, slew, place the region of interest, calibrate the exposure, capture, then do the same on a nearby reference star. With a mount N.I.N.A. can slew it needs no one present; pointed by hand it asks you to confirm each slew. Progress is written to a nightly CSV, so a run can be stopped and picked up later.

The full manual is inside the plugin, under the **Documentation** tab in the plugin options. No internet needed. The old Advanced Sequencer instructions still work, but the panel does everything they did.

## Links ##

Background on double star observing: [Boyce Astro](https://boyce-astro.org/videos/astrometry/). Frames can be reduced with [The Speckle Toolbox](https://www.dropbox.com/s/wmr58i9owd2lvja/STB%201.14.zip?dl=0) ([manual](http://www.jdso.org/volume13/number1/Harshaw_52_67.pdf)) and published through the [JDSO](http://www.jdso.org/). The [Stelar group](https://stelar.groups.io/g/main) shares target lists and [guides](https://stelar.groups.io/g/main/files/Useful_Guides_Documents).

Thank you to the members and friends of STELAR, and to Observable Space for telescope access that has produced many papers.

Ideas or issues: contact Nick in the [N.I.N.A. Discord](https://discord.gg/rWRbVbw), tagging @nickholland. To buy Nick a whisky: [click here](https://www.paypal.com/paypalme/NickHardyHolland)
")]


// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]
// [Unused]
//[assembly: AssemblyConfiguration("")]
// [Unused]
[assembly: AssemblyTrademark("")]
// [Unused]
[assembly: AssemblyCulture("")]