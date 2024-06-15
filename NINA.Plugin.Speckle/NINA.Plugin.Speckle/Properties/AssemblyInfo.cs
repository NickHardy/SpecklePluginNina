using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// [MANDATORY] The following GUID is used as a unique identifier of the plugin. Generate a fresh one for your plugin!
[assembly: Guid("543704e0-51ac-492c-4e94-3c85f2c07e22")]

// [MANDATORY] The assembly versioning
//Should be incremented for each new release build of a plugin
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]

// [MANDATORY] The name of your plugin
[assembly: AssemblyTitle("Speckle Interferometry")]
// [MANDATORY] A short description of your plugin
[assembly: AssemblyDescription("This plugin automates the acquisition of speckle interferometry data for closely seperatd objects.")]


// The following attributes are not required for the plugin per se, but are required by the official manifest meta data

// Your name
[assembly: AssemblyCompany("Nick Hardy & Leon Bewersdorff")]
// The product name that this plugin is part of
[assembly: AssemblyProduct("Speckle Interferometry")]
[assembly: AssemblyCopyright("")]

// The minimum Version of N.I.N.A. that this plugin is compatible with
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.3005")]

// The license your plugin code is using
[assembly: AssemblyMetadata("License", "MPL-2.0")]
// The url to the license
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
// The repository where your pluggin is hosted
[assembly: AssemblyMetadata("Repository", "https://bitbucket.org/NickHardy/nina.plugin.speckle/src/main/")]


// The following attributes are optional for the official manifest meta data

//[Optional] Your plugin homepage URL - omit if not applicaple
[assembly: AssemblyMetadata("Homepage", "https://stelar.groups.io/")]

//[Optional] Common tags that quickly describe your plugin
[assembly: AssemblyMetadata("Tags", "Speckle,Interferometry,Acquisition")]

//[Optional] A link that will show a log of all changes in between your plugin's versions
[assembly: AssemblyMetadata("ChangelogURL", "https://bitbucket.org/NickHardy/nina.plugin.speckle/commits/branch/main")]

//[Optional] The url to a featured logo that will be displayed in the plugin list next to the name
[assembly: AssemblyMetadata("FeaturedImageURL", "https://bitbucket.org/NickHardy/nina.plugin.speckle/downloads/SpeckleThumb.png")]
//[Optional] A url to an example screenshot of your plugin in action
[assembly: AssemblyMetadata("ScreenshotURL", "https://bitbucket.org/NickHardy/nina.plugin.speckle/downloads/SpeckleOrbits.png")]
//[Optional] An additional url to an example example screenshot of your plugin in action
[assembly: AssemblyMetadata("AltScreenshotURL", "https://bitbucket.org/NickHardy/nina.plugin.speckle/downloads/ListSequence.png")]
//[Optional] An in-depth description of your plugin
[assembly: AssemblyMetadata("LongDescription", @"#Speckle Interferometry data acquisition plugin#

This plugin automates the acquisition of speckle interferometry data for closely separated objects, enabling imaging below the seeing limit, such as for double stars. 
Speckle Imaging, similar to Lucky Imaging, can be performed even on nights with a full moon, making it an excellent alternative when deep-sky object (DSO) imaging is less ideal.
Speckle interferometry can be achieved with almost any telescope, though a large aperture and long focal length will help resolve fainter targets with smaller separations.

You can create a separate profile for speckle targets. The key is to capture as many images as possible in a short timeframe with a cropped region of interest (ROI) on the sensor. 
Typically, the cropped ROI will be 256x256 or 512x512 pixels, depending on the focal length and pixel size. There's no need for guiding or dithering, similar to Lucky Imaging.

Detailed information on double star observing and speckle interferometry can be found here:  
[https://boyce-astro.org/videos/astrometry/](https://boyce-astro.org/videos/astrometry/)

Create a separate folder for speckle images and set the filepattern to something like this:
$$DATEMINUS12$$\$$SEQUENCETITLE$$\$$TARGETNAME$$\$$FILTER$$\$$EXPOSURETIME$$\$$FRAMENR$$_$$TARGETNAME$$_$$NOTE$$

## Plugin Instructions ##

* Speckle Target List Container  
  This is the main container for your targets. Here, you can load a list of targets to observe throughout the night.
  Create and Select Speckle Target Template: Enter your preferred values for the target list.
  Load Target Set: Press 'Load target set' to select the CSV file containing the targets. The provided CSV will be filtered based on your location and the targets' altitude during the night. The targets should reach an altitude between 40 and 85 degrees, optimal for speckle image acquisition, though this range can be adjusted in the options to suit your needs.
  All loaded targets will be checked for the best imaging time and sorted accordingly.

  When you start the sequence, the container will load the first target using the chosen template, and it will fill in some values into the instructions, like 'wait for time' and exposure times.
  It will also retrieve a reference star nearby the target and use the same template to create the sequence.
  The reference star is retrieved from the [Simbad Astronomical Database](http://simbad.u-strasbg.fr/simbad/).
  It must be an SAO single star, near the target and preferebly a little brighter than the target. The plugin will attempt to match the color with the target star as closely as possible.
  After the target and reference containers have run through, it will remove the containers and load the next target until it has finished all targets or until a condition ends the loop.
  When the next target is more than 5 minutes away, it will select a previous target which is highest in altitude and has had the least cycles to fill up the time.

* Speckle Target Container [example](https://bitbucket.org/NickHardy/nina.plugin.speckle/downloads/Speckle_Target_Container.template.json)  
  Use this container to add the instructions that each speckle target will use. Save it as a template to use it for targets within the Speckle Target List Container.
  You will need to set the width and height for the region of interest. Typically 256x256 or 512x512, as mentioned above.. Adjust these settings while your camera is connected. The x and y coordinates are the left upper corner of the ROI position.
  Note: some cameras image faster when the width and height are linked to the dimensions of the camera. When the toggle is enabled, it will adjust the width or height accordingly.

* Calculate Roi Position  
  Depending on the accuracy of your mount, the target will not always be centered exactly.
  This instruction can be used to platesolve the image and set the position of the ROI image to fit the target in the Speckle Target Container.
  Make sure the focal length and pixel size are filled in correctly.

* Calculate exposure time  
  Use this instruction to calculate the exposure needed to get enough signal in your speckle images for the fainter object to be resolved after processing. Make sure to enter all the correct values for the scope in the options.
  When the instruction finds a good exposure time, it will copy that time into the 'Take Video Roi Exposures' and 'Take Roi Exposures' instructions in the same Speckle Target Container.

* Take Video Roi Exposures  
  Supported for cameras from QHY, ZWO, Altair Astro and Astpancam. 
  This will rapidly take exposures using video mode of the target Roi using the Roi settings in the Speckle Target Container.
  To keep the speed up, it will not show every image in the imaging tab. Rather it will show the first and last image and every Nth image (as set in the plugin options).
  Also, the images will not show up in the image history or the HFR history. The speed of capturing images will depend on the speed of the imaging pc.
  Images could potentially show 8 bit bitdepth in the statistics instead of the actual 16 bit.

* Take Roi Exposures  
  This will take rapid single exposures of the target Roi using the Roi settings in the Speckle Target Container, not using video mode.
  To keep the speed up, it will not show every image in the imaging tab. Rather it will show the first and last image and every Nth image (as set in the plugin options).
  Also, the images will not show up in the image history or the HFR history. The speed of capturing images will depend on the speed of the imaging pc.
  Images could potentially show 8 bit bitdepth in the statistics instead of the actual 16 bit.
 
* Center on StarCluster  
  Speckle interferometry is typically done using very long focal lengths and a small FOV.
  Sometimes there are not enough stars to platesolve correctly. This instruction will search for a nearby star cluster, slew to it, and platesolve there.
  You can for instance auto focus on the starcluster, or you can slew back to the target and it should be in the FOV. If platesolving works using a full image on the target, you don't need this instruction.

* Synch on StarCluster  
  Speckle interferometry is typically done using very long focal lengths and a small FOV.
  Sometimes there are not enough stars to platesolve correctly. This instruction will search for a nearby star cluster, slew to it, and platesolve there.
  If it fails, it will slew to the next starcluster within the given radius until it platesolves successfully.
  Afterwards, you can slew to the target and it should be in the field of view. If platesolving works using a full image on the target, you don't need this instruction.

## Target lists ##
  Here are a few example lists for different telescope apertures:
  + [6-inch telescopes and larger](https://bitbucket.org/NickHardy/nina.plugin.speckle/downloads/GdsSpeckleTargetList6inch.csv)
  + [10-inch telescopes and larger](https://bitbucket.org/NickHardy/nina.plugin.speckle/downloads/GdsSpeckleTargetList10inch.csv)

For finding targets and processing data, the following programs can be used, courtesy of Dave Rowe:
* [GDS](https://drive.google.com/file/d/1e72E2sfvVnsYTZp0kiZyVdDZNkeV2BLB/view?usp=sharing)  
  This program contains data from GAIA DR2 and can be used to select targets for imaging.
* [The Speckle Toolbox](https://www.dropbox.com/s/wmr58i9owd2lvja/STB%201.14.zip?dl=0)  
  This program can be used to process the speckle interferometry data. Detailed instructions on how to use it: [The Speckle Toolbox - manual](http://www.jdso.org/volume13/number1/Harshaw_52_67.pdf)  
  Currently, there's no way to directly submit data to a catalog of double stars, such as the WDS catalog. The way to get data into this catalog is to write an academic paper en submit it to the [JDSO](http://www.jdso.org/).

Thank you to the members and friends of STELAR for all the input given to create this plugin. Thank you to PlaneWave Instruments for access to multiple of their telescopes to use this plugin with, which has resulted in many papers.

We'd also like to thank Jocelyn Serot, for his help with image processing. You can visit his [website](http://www.astrosurf.com/legalet/Astro/Welcome.html) or try out his [LiveSpeckle plugin](http://www.astrosurf.com/legalet/Astro/LiveSpeckle.html) for [Genika Astro](https://airylab.com/genika-astro/).
This part of the plugin is still in development and not yet visible.

If you have any ideas or want to report an issue, please contact Nick in the [Nina discord server](https://discord.gg/rWRbVbw) by tagging @nickholland.

If you would like to buy Nick a whisky: [click here](https://www.paypal.com/paypalme/NickHardyHolland)

")]


// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]
// [Unused]
[assembly: AssemblyConfiguration("")]
// [Unused]
[assembly: AssemblyTrademark("")]
// [Unused]
[assembly: AssemblyCulture("")]