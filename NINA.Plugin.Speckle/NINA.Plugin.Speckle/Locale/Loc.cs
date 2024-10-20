#region "copyright"

/*
    Copyright � 2016 - 2021 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Locale;
using NINA.Core.Utility;
using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Resources;
using System.Windows.Data;

namespace NINA.Plugin.Speckle.Locale {

    // This is not in use yet. I was hoping to make it intenational, but Nina is not ready for that yet.

    [Export(typeof(ILoc))]
    [JsonObject(MemberSerialization.OptIn)]
    public class Loc : BaseINPC, ILoc {
        private ResourceManager _locale;
        private CultureInfo _activeCulture;

        private static readonly Lazy<Loc> lazy =
         new Lazy<Loc>(() => new Loc());

        private Loc() {
            _locale = new ResourceManager("NINA.Plugin.Speckle.Locale.Locale", typeof(Loc).Assembly);
        }

        public void ReloadLocale(string culture) {
            using (MyStopWatch.Measure()) {
                try {
                    _activeCulture = new CultureInfo(culture);
                } catch (Exception ex) {
                    Logger.Error(ex);
                }
                RaiseAllPropertiesChanged();
            }
        }

        public static Loc Instance { get { return lazy.Value; } }

        public string this[string key] {
            get {
                if (key == null) {
                    return string.Empty;
                }
                return this._locale?.GetString(key, this._activeCulture) ?? $"MISSING LABEL {key}";
            }
        }
    }

    public class LocExtension : Binding {

        public LocExtension(string name) : base($"[{name}]") {
            this.Mode = BindingMode.OneWay;
            this.Source = Loc.Instance;
        }
    }
}