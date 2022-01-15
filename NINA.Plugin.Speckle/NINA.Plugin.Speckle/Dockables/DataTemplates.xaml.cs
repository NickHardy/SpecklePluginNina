using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace NINA.Plugin.Speckle.Dockables {

    [Export(typeof(ResourceDictionary))]
    public partial class DataTemplates : ResourceDictionary {

        public DataTemplates() {
            InitializeComponent();
        }

    }
}
