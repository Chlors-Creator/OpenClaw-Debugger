(function () {
  'use strict';

  window.OpenClawPluginState = {
    create() {
      return {
        installed: [],
        loaded: false,
        loading: false,
        categoryLookupLoading: false,
        categoryLookupController: null,
        mutating: false,
        loadError: '',
        loadGeneration: 0,
        loadController: null
      };
    }
  };
})();
