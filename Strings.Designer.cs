﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.17929
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace NeverAway {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "4.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class Strings {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Strings() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("NeverAway.Strings", typeof(Strings).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to It&apos;s {0} and an error occurred! Message: {1} {2}.
        /// </summary>
        internal static string errorMessage {
            get {
                return ResourceManager.GetString("errorMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Some error occurred during application initialization. I&apos;m going to rethrow it now so you can see the ugly details..
        /// </summary>
        internal static string InitializationError {
            get {
                return ResourceManager.GetString("InitializationError", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Warning! You will currently never show as &apos;away&apos; in MOC!.
        /// </summary>
        internal static string neverAwayText {
            get {
                return ResourceManager.GetString("neverAwayText", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Everything is working as normal..
        /// </summary>
        internal static string normalAwayText {
            get {
                return ResourceManager.GetString("normalAwayText", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to &quot;It&apos;s {0} and I have pressed {1} key {2} times so far...&quot;.
        /// </summary>
        internal static string statusMessage {
            get {
                return ResourceManager.GetString("statusMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Status.
        /// </summary>
        internal static string tipTitle {
            get {
                return ResourceManager.GetString("tipTitle", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Exit.
        /// </summary>
        internal static string trayMenuExit {
            get {
                return ResourceManager.GetString("trayMenuExit", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Never Away?.
        /// </summary>
        internal static string trayMenuNeverAway {
            get {
                return ResourceManager.GetString("trayMenuNeverAway", resourceCulture);
            }
        }
    }
}