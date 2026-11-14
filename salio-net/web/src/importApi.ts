import { API } from "./auth";
import { apiFetch, problemText } from "./api";

// Shared by the import screen and the import history.

export type UndoResult = {
  undoneCreated: number;
  undoneUpdated: number;
  kept: { productId: string; sku: string; name: string; reason: string }[];
};

// "1 new part", "3 new parts".
export function counted(n: number, one: string, many: string) {
  return `${n.toLocaleString("en-KE")} ${n === 1 ? one : many}`;
}

// The server's own words for a refusal: "detail" is written for a person.
export function refusalText(response: Response, fallback: string) {
  return problemText(response, fallback);
}

// Undo one import. The server reverses what it can and says what it kept.
export async function undoImport(
  shopId: string,
  batchId: string,
): Promise<{ result?: UndoResult; problem?: string; expired?: boolean }> {
  try {
    const response = await apiFetch(`${API}/api/imports/${batchId}/undo?organizationId=${shopId}`, {
      method: "POST",
      credentials: "include",
    });

    if (response.status === 401) {
      return { expired: true };
    }

    if (!response.ok) {
      return { problem: await refusalText(response, "Import not undone") };
    }

    return { result: await response.json() };
  } catch {
    return { problem: "Can't reach Salio" };
  }
}

