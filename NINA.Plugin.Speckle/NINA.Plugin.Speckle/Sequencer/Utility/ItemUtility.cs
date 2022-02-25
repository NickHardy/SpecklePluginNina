#region "copyright"

/*
    Copyright © 2016 - 2021 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Plugin.Speckle.Sequencer.Container;
using NINA.Astrometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NINA.Sequencer.Container;
using NINA.Core.Utility;

namespace NINA.Plugin.Speckle.Sequencer.Utility {

    public class ItemUtility {

        public static SpeckleTargetContainer RetrieveSpeckleContainer(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as SpeckleTargetContainer;
                if (container != null) {
                    return container;
                } else {
                    return RetrieveSpeckleContainer(parent.Parent);
                }
            } else {
                return null;
            }
        }

        public static ObservableRectangle RetrieveSpeckleTargetRoi(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as SpeckleTargetContainer;
                if (container != null) {
                    return container.SubSampleRectangle;
                } else {
                    return RetrieveSpeckleTargetRoi(parent.Parent);
                }
            } else {
                return null;
            }
        }

        public static string RetrieveSpeckleTitle(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as SpeckleTargetContainer;
                if (container != null) {
                    return container.Title;
                } else {
                    return RetrieveSpeckleTitle(parent.Parent);
                }
            } else {
                return null;
            }
        }

        public static InputTarget RetrieveSpeckleTarget(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as SpeckleTargetContainer;
                if (container != null) {
                    return container.Target;
                } else {
                    return RetrieveSpeckleTarget(parent.Parent);
                }
            } else {
                return null;
            }
        }

    }
}