import { createContext, useReducer } from "react";
import {
  initialClients,
  initialMachines,
  initialTravaux,
  initialInventaire,
} from "../data/initialData";

// eslint-disable-next-line react-refresh/only-export-components
export const AppContext = createContext(null);

const initialState = {
  clients: initialClients,
  machines: initialMachines,
  travaux: initialTravaux,
  inventaire: initialInventaire,
};

function reducer(state, action) {
  switch (action.type) {
    // --- CLIENTS ---
    case "ADD_CLIENT":
      return {
        ...state,
        clients: [
          ...state.clients,
          { ...action.payload, id: Date.now() },
        ],
      };
    case "UPDATE_CLIENT":
      return {
        ...state,
        clients: state.clients.map((c) =>
          c.id === action.payload.id ? action.payload : c
        ),
      };
    case "DELETE_CLIENT":
      return {
        ...state,
        clients: state.clients.filter((c) => c.id !== action.payload),
      };

    // --- MACHINES ---
    case "ADD_MACHINE":
      return {
        ...state,
        machines: [
          ...state.machines,
          { ...action.payload, id: Date.now() },
        ],
      };
    case "UPDATE_MACHINE":
      return {
        ...state,
        machines: state.machines.map((m) =>
          m.id === action.payload.id ? action.payload : m
        ),
      };
    case "DELETE_MACHINE":
      return {
        ...state,
        machines: state.machines.filter((m) => m.id !== action.payload),
      };

    // --- TRAVAUX ---
    case "ADD_TRAVAIL":
      return {
        ...state,
        travaux: [
          ...state.travaux,
          { ...action.payload, id: Date.now() },
        ],
      };
    case "UPDATE_TRAVAIL":
      return {
        ...state,
        travaux: state.travaux.map((t) =>
          t.id === action.payload.id ? action.payload : t
        ),
      };
    case "DELETE_TRAVAIL":
      return {
        ...state,
        travaux: state.travaux.filter((t) => t.id !== action.payload),
      };

    // --- INVENTAIRE ---
    case "ADD_ARTICLE":
      return {
        ...state,
        inventaire: [
          ...state.inventaire,
          { ...action.payload, id: Date.now() },
        ],
      };
    case "UPDATE_ARTICLE":
      return {
        ...state,
        inventaire: state.inventaire.map((a) =>
          a.id === action.payload.id ? action.payload : a
        ),
      };
    case "DELETE_ARTICLE":
      return {
        ...state,
        inventaire: state.inventaire.filter((a) => a.id !== action.payload),
      };

    default:
      return state;
  }
}

export function AppProvider({ children }) {
  const [state, dispatch] = useReducer(reducer, initialState);
  return (
    <AppContext.Provider value={{ state, dispatch }}>
      {children}
    </AppContext.Provider>
  );
}
