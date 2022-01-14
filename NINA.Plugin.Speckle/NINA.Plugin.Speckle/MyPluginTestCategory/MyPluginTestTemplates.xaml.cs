using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace MyPlugin.MyPluginTestCategory {

    [Export(typeof(ResourceDictionary))]
    public partial class PluginTestItemTemplate : ResourceDictionary {

        public PluginTestItemTemplate() {
            InitializeComponent();
        }
    }
}