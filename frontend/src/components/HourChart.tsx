import { Bar } from 'react-chartjs-2';
import {
  Chart as ChartJS, CategoryScale, LinearScale, BarElement, Tooltip, Legend
} from 'chart.js';

ChartJS.register(CategoryScale, LinearScale, BarElement, Tooltip, Legend);

interface Props {
  labels: string[];
  data: number[];
  label?: string;
}

export default function HourChart({ labels, data, label = 'Hours' }: Props) {
  return (
    <Bar
      data={{
        labels,
        datasets: [
          {
            label,
            data,
            backgroundColor: '#c9822e',
            borderRadius: 4,
            barThickness: 26
          }
        ]
      }}
      options={{
        responsive: true,
        plugins: { legend: { display: false } },
        scales: {
          y: { beginAtZero: true, grid: { color: '#e3ded2' } },
          x: { grid: { display: false } }
        }
      }}
    />
  );
}
