import { useState, useEffect } from "react";

type Product = {
  id: string;
  sku: string;
  name: string;
  sellPriceMinor: number;
  binLocation: string | null;
};

const ORG_ID = "00000000-0000-0000-0000-000000000001";

export default function App() {
  const [products, setProducts] = useState<Product[]>([]);

  useEffect(() => {
    fetch(`http://localhost:5077/api/products?organizationId=${ORG_ID}`)
      .then((response) => response.json())
      .then((data) => setProducts(data));
  }, []);

  return (
    <div>
      <h1>Salio Pos
        
      </h1>
      {products.map((product) => (
        <div key={product.id}>
          {product.name} — KES {product.sellPriceMinor / 100}
        </div>
      ))}
    </div>
  );
}