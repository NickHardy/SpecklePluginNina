#region "copyright"

/*
    Copyright (c) 2026 Nick Hardy and Leon Bewersdorff

    This file is part of the Speckle Interferometry plugin for
    N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    Released under the MIT License. See LICENSE.txt in the repository
    root, or https://opensource.org/licenses/MIT
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