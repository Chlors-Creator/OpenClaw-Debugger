(function () {
  'use strict';

  window.OpenClawPluginState = {
    create() {
      return {
        installed: [],
        searchResponse: null,
        loaded: false,
        loading: false,
        searching: false,
        mutating: false,
        loadGeneration: 0,
        searchGeneration: 0,
        loadController: null,
        searchController: null
      };
    }
  };
})();
