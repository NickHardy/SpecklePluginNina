using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// [MANDATORY] The following GUID is used as a unique identifier of the plugin. Generate a fresh one for your plugin!
[assembly: Guid("543704e0-51ac-492c-4e94-3c85f2c07e22")]

// [MANDATORY] The assembly versioning
//Should be incremented for each new release build of a plugin
[assembly: AssemblyVersion("1.0.3.7")]
[assembly: AssemblyFileVersion("1.0.3.7")]

// [MANDATORY] The name of your plugin
[assembly: AssemblyTitle("Speckle Interferometry")]
// [MANDATORY] A short description of your plugin
[assembly: AssemblyDescription("This plugin automates the acquisition of speckle interferometry data for close multi-star systems.")]


// The following attributes are not required for the plugin per se, but are required by the official manifest meta data

// Your name
[assembly: AssemblyCompany("NickHardy")]
// The product name that this plugin is part of
[assembly: AssemblyProduct("Speckle Interferometry")]
[assembly: AssemblyCopyright("")]

// The minimum Version of N.I.N.A. that this plugin is compatible with
[assembly: AssemblyMetadata("MinimumApplicationVersion", "2.0.1.2001")]

// The license your plugin code is using
[assembly: AssemblyMetadata("License", "MPL-2.0")]
// The url to the license
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
// The repository where your pluggin is hosted
[assembly: AssemblyMetadata("Repository", "https://bitbucket.org/NickHardy/nina.plugin.speckle/src/main/")]


// The following attributes are optional for the official manifest meta data

//[Optional] Your plugin homepage URL - omit if not applicaple
[assembly: AssemblyMetadata("Homepage", "https://www.fairborninstitute.org/")]

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

**Disclaimer: Currently there is no way to submit data except for writing a paper and getting it published in JDSO. I am working on a way for amateurs to submit data and will let people know when that is ready.**  

This plugin automates the acquisition of speckle interferometry data for close multi-star systems.
Speckle imaging can be done on nights with a full moon, so it's a great alternative for when DSO imaging is less ideal.
You can try this with just about any telescope, but of course bigger is better. Ideally you want big aperture and long focal length.
But make sure you can still platesolve your full image or you'll need pinpoint precision when targeting.  
It's best if you create a separate profile for Speckle targets. There are some things you don't need for Speckle imaging.
There's no need to guide or dither. just get rapid small images for the star. Usually the image will be 256x256 or 512x512, depending on the telescope and camera.
Also create a separate folder for speckle images and set the filepattern to something like this:
$$DATEMINUS12$$\$$SEQUENCETITLE$$\$$TARGETNAME$$\$$FILTER$$\$$EXPOSURETIME$$\$$TARGETNAME$$_$$FRAMENR$$

Videos explaning double star observing and speckle interferometry can be found here:  
[https://boyce-astro.org/videos/astrometry/](https://boyce-astro.org/videos/astrometry/)

## Instructions ##

* Speckle Target Container [example](https://bitbucket.org/NickHardy/nina.plugin.speckle/downloads/Speckle_Target_Container.template.json)  
  Use this container to add the speckle specific instructions. Save it as a template to use it for targets within the Speckle Target List Container.
  You will need to set the width and height for the region of interest. Typically 256x256 or 512x512, depending on your setup. Adjust these settings while your camera is connected.
  Some cameras image faster when the width and height are linked to the dimensions of the camera. When the toggle is enabled it will adjust the width or height accordingly.
  The x and y coordinates are the left upper corner of the Roi position.

* Calculate Roi Position  
  Depending on the accuracy of your setup, the target will not always be centered exactly.
  This instruction can be used to platesolve the image and set the position of the Roi image to fit the target in the Speckle Target Container.
  Make sure the focal length and pixelsize are filled in correctly.

* Calculate exposure time (Still under development)  
  Use this instruction to check if your Roi image is not to bright. The speckle star should not exceed 1/3 of the camera ADU.
  The max time will be set to the ExposureTime set for the target in the list container. It will check the brightness and lower the exposuretime until it reaches the target ADU.
  When it finds a good exposure time it will copy that time into the 'Take Video Roi Exposures' and 'Take Roi Exposures' instructions in the same Speckle Target Container.

* Take Video Roi Exposures  
  Only for QHY, ZWO, Altair Astro and Astpancam cameras. It will take rapid exposures using video mode of the target Roi using the Roi settings in the Speckle Target Container.
  To keep the speed up, it will not show every image in the imaging tab. Rather it will show the first and last image and every Nth image set in the plugin options.
  Also the images will not show up in the image history or the Hfr history. The speed of capturing images will depend on the speed of the imaging pc.
  Images could potentially show 8 bit bitdepth in the statistics instead of the actual 16 bit.

* Take Roi Exposures  
  Fit for most astro cameras. DSLRs can't do this. It will take rapid single exposures of the target Roi using the Roi settings in the Speckle Target Container.
  To keep the speed up, it will not show every image in the imaging tab. Rather it will show the first and last image and every Nth image set in the plugin options.
  Also the images will not show up in the image history or the Hfr history. The speed of capturing images will depend on the speed of the imaging pc.
  Images could potentially show 8 bit bitdepth in the statistics instead of the actual 16 bit.

* Speckle Target List Container  
  This is the main target container. Here you can load a list of targets to run through the night.
  First create and choose the Speckle Target Template and enter the preferred values for the target list.
  Next press Load target set to select the csv file with the targets.
  The csv you provide will be filtered based on your location and the targets altitude during the night.
  The target needs to reach an altitude between 40 and 85 degrees during the night, which is best for speckle image aquisition, but can be altered in the options to suite your situation.
  All loaded targets will be checked for the best imaging time and sorted accordingly.  

  When you start the sequence it will load the first target using the chosen template and it will fill in some values into the instructions, like Wait for time, Exposure times.
  It will also retrieve a reference star nearby the target and use the same Template to create the sequence. The reference star is needed as a baseline for the target double star.
  The reference star is retrieved from the [Simbad Astronomical Database](http://simbad.u-strasbg.fr/simbad/).
  It must be an SAO single star, near the target and preferebly a little brighter than the target.
  After the target and reference containers have run through, it will remove the containers and load the next target until it has finished all targets or until a condition ends the loop.
  When the next target is more than 5 minutes away, it will select a previous target which is highest in altitude and has had the least cycles to fill up the time.

* Center on StarCluster  
  Speckle interferometry is typically done using really long focal lengths and a small fov.
  Sometimes there are not enough stars to platesolve correctly. This instruction will search for a nearby star cluster, slew to it and platesolve there.
  You can for instance auto focus on the starcluster or you can slew back to the target and it should be in the field of view. If platesolving works using a full image on the target, you won't need this instruction.

* Synch on StarCluster  
  Speckle interferometry is typically done using really long focal lengths and a small fov.
  Sometimes there are not enough stars to platesolve correctly. This instruction will search for a nearby star cluster, slew to it and platesolve there.
  If it fails it will slew to the next starcluster within the given radius until it platesolves successfully.
  Afterwards you can slew to the target and it should be in the field of view. If platesolving works using a full image on the target, you won't need this instruction.

## Target lists ##
  Here are a few lists based on telescope diameter:
  + [6 inch telescopes and larger](https://bitbucket.org/NickHardy/nina.plugin.speckle/downloads/GdsSpeckleTargetList6inch.csv)
  + [10 inch telescopes and larger](https://bitbucket.org/NickHardy/nina.plugin.speckle/downloads/GdsSpeckleTargetList10inch.csv)

For listing targets and processing data the following programs can be used by courtesy of Dave Rowe:
* [GDS](https://drive.google.com/file/d/1e72E2sfvVnsYTZp0kiZyVdDZNkeV2BLB/view?usp=sharing)  
  This program contains data from Gaia and can be used to select targets for imaging.
* [The Speckle Toolbox](https://www.dropbox.com/s/wmr58i9owd2lvja/STB%201.14.zip?dl=0)  
  This program can be used to process the data. Here are detailed instructions on how to use it: [The Speckle Toolbox - manual](http://www.jdso.org/volume13/number1/Harshaw_52_67.pdf)  
  Currently there's no way to upload data to the WDS catalog. The way to get data into the catalog is to write an academic paper en submit it to [jdso](http://www.jdso.org/).
  Hopefully in future it will be possible to upload data for submission to the WDS catalog. (Still working on that)

A big thank you goes out to Leon(@lbew#3670) for testing this plugin with me. 
Also many thanks to the members and friends of Fairborn Institute for all the input given to create this plugin and to Planewave for trusting me with two of their scopes.

I'd also like to thank Jocelyn Serot, for his help with image processing. You can visit his [website](http://www.astrosurf.com/legalet/Astro/Welcome.html) or try out his [LiveSpeckle plugin](http://www.astrosurf.com/legalet/Astro/LiveSpeckle.html) for [Genika Astro](https://airylab.com/genika-astro/).
This part of the plugin is still in development and not yet visible.

If you have any ideas or want to report an issue, please contact me in the [Nina discord server](https://discord.gg/rWRbVbw) and tag me: @NickHolland#5257 

If you would like to buy me a whisky: [click here](https://www.paypal.com/paypalme/NickHardyHolland)

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