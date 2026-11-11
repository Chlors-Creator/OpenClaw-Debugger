(function () {
  'use strict';

  window.OpenClawSkillState = {
    create() {
      return {
        installed: [],
        installedRaw: null,
        listFormatError: false,
        loaded: false,
        loading: false,
        mutating: false,
        loadError: '',
        loadGeneration: 0,
        loadController: null,
        detailGeneration: 0,
        detailController: null
      };
    }
  };
})();
