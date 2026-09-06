#region "copyright"

/*
    Copyright (c) 2026 Nick Hardy and Leon Bewersdorff

    This file is part of the Speckle Interferometry plugin for
    N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    Released under the MIT License. See LICENSE.txt in the repository
    root, or https://opensource.org/licenses/MIT
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace NINA.Plugin.Speckle.Sequencer.Container {

    [Export(typeof(ResourceDictionary))]
    public partial class Datatemplates {

        public Datatemplates() {
            InitializeComponent();
        }

        private void DataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
            if (sender is System.Windows.Controls.DataGrid dataGrid && e.AddedItems.Count > 0)
                dataGrid.ScrollIntoView(e.AddedItems[0]);
        }

        private void DataGridCell_Selected(object sender, RoutedEventArgs e) {
            if (e.OriginalSource.GetType() == typeof(System.Windows.Controls.DataGridCell)) {
                System.Windows.Controls.DataGrid grd = (System.Windows.Controls.DataGrid)sender;
                grd.BeginEdit(e);
            }
        }

        private void DataGridRow_Selected(object sender, RoutedEventArgs e) {
            if (e.OriginalSource.GetType() == typeof(DataGridRow)) {
                System.Windows.Controls.DataGrid grd = (System.Windows.Controls.DataGrid)sender;
                grd.BeginEdit(e);
            }
        }

    }
}