(function () {
  'use strict';

  window.OpenClawPluginState = {
    create() {
      return {
        installed: [],
        loaded: false,
        loading: false,
        mutating: false,
        loadError: '',
        loadGeneration: 0,
        loadController: null
      };
    }
  };
})();
