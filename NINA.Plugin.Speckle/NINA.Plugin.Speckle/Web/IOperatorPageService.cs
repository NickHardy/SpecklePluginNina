using System;
using System.Collections.Generic;

namespace NINA.Plugin.Speckle.Web {

    public interface IOperatorPageService {

        OperatorState State { get; }

        bool IsRunning { get; }

        int Port { get; }

        IReadOnlyList<string> Urls { get; }

        string CurrentTargetName { get; }

        event EventHandler Changed;

        bool Acquire(string owner);

        void Release(string owner);

        void ApplyOptions();

        void Stop();
    }
}
