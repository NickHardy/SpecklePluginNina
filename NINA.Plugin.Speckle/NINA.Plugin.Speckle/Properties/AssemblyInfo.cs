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
[assembly: AssemblyMetadata("License", "MPL-2.0")]
// The url to the license
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
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
[assembly: AssemblyMetadata("ScreenshotURL", "https://raw.githubusercontent.com/NickHardy/SpecklePluginNina/version3/screenshots/plan.png")]
//[Optional] An additional url to an example example screenshot of your plugin in action
[assembly: AssemblyMetadata("AltScreenshotURL", "https://raw.githubusercontent.com/NickHardy/SpecklePluginNina/version3/screenshots/fringes.png")]
//[Optional] An in-depth description of your plugin
[assembly: AssemblyMetadata("LongDescription", @"#Speckle Interferometry data acquisition plugin#

Acquisition of speckle interferometry data. This plugin supports loading target lists with many targets to be worked through over one or more nights. When observing, the plugin handles target scheduling, slews to the next best target, places a region of interest, and takes many short exposures to resolve features below the seeing limit. 

Built around automatic capture with the highest possible cadence, and includes a manual mode with as little input required as possible to be used on large manual telescopes, such as the 2.5-meter on Mt. Wilson. Use of two cameras, so that a dedicated science camera can improve sensitivity in bandpasses of interest, is also supported.

## Plugin Dockable ##

Everything runs from a dockable panel on the Imaging tab. Add it from the panel menu:

![Adding the panel](https://raw.githubusercontent.com/NickHardy/SpecklePluginNina/version3/screenshots/panel-add.png)

Drag its title bar to pop it out into its own window to use the full screen, as the rest of the NINA UI, notably the sequencer, does not have to be used for operation.

## Planning ##

The plan page holds the loaded target lists, the observation order, and a queue. Pin what runs next, reorder it, drop a target, or add a star by hand.

![The plan page](https://raw.githubusercontent.com/NickHardy/SpecklePluginNina/version3/screenshots/plan.png)

## Running ##

The run page works through one target at a time: pick it, slew, place the region of interest on the star, calibrate the exposure, capture the frames, then do the same on a nearby reference star.

When using this plugin in manual mode, configurable in the options, the flow stops at each point that needs operator judgement. With a mount N.I.N.A. can slew, a toggle in the plugin options lets the whole chain run through completely unattended, observing up to typically around 300 targets a night depending on hardware capability.

![Slewing to a target](https://raw.githubusercontent.com/NickHardy/SpecklePluginNina/version3/screenshots/slewing.png)

Filters, exposure time and frame counts can be changed at the confirmation card, for the capture that is about to start.

![Confirming the capture settings](https://raw.githubusercontent.com/NickHardy/SpecklePluginNina/version3/screenshots/capture.png)

## Fringes ##

The mean power spectrum and its autocorrelation build up beside the frames as a run captures, so a resolved pair can be confirmed while the data is still being taken. Both are written as JPEG files alongside the saved frames.

![The power spectrum beside the frames](https://raw.githubusercontent.com/NickHardy/SpecklePluginNina/version3/screenshots/fringes.png)

## Additionally ##

* Video mode capture on QHY, ZWO, Altair and Touptek cameras. A sustained 108 fps on a 1024 x 1024 region with an ASI585, and 49.8 of a possible 50 fps at 20 ms exposures.
* Since this relies on high framerate capture, this plugin includes a camera benchmark to time capture, with every driver call and a report being written to the N.I.N.A. log directory.

Advanced:
* An operator webpage the plugin serves itself, for manual large telescopes such as on Mt. Wilson. Off by default.
* Two camera setups behind a flip mirror, and scheduling around a parked dome slit.
* Legacy advanced sequencer instructions for special edge cases.

## Getting started ##

1. Add the ""Speckle"" panel to the Imaging tab.
2. Set your telescope and camera under Options > Plugins > SpeckleInterferometry.
3. Load a target list, then press Start.

Progress is written to a nightly CSV, so a run can be stopped and picked up again later. Further docmentation is inside the plugin, under the Documentation tab in the plugin options.

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